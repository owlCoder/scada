using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Microsoft.Extensions.DependencyInjection;
using IntegrationTests.Support;
using ProtocolTests.Support;
using Protocols.Iec104;
using Xunit;

namespace ProtocolTests.Iec104;

public sealed class Iec104WireTests
{
    private static async Task<RawIec104Client> StartedClientAsync(int port)
    {
        var client = await RawIec104Client.ConnectAsync(port);
        await client.SendAsync(RawIec104Client.Unnumbered(Apci.StartDtAct));
        var con = await client.ReceiveApduAsync();
        Assert.Equal(ApduFrameKind.Unnumbered, con.Control.Kind);
        Assert.Equal(Apci.StartDtCon, con.Control.UnnumberedFunction);
        return client;
    }

    private static byte[] Interrogation(int commonAddress, byte qoi = 20) =>
        RawIec104Client.AsduBytes(Iec104TypeId.C_IC_NA_1, CauseOfTransmission.ActivationRequest, commonAddress, (0, [qoi]));

    private static byte[] DoubleCommand(int commonAddress, int ioa, byte dco) =>
        RawIec104Client.AsduBytes(Iec104TypeId.C_DC_NA_1, CauseOfTransmission.ActivationRequest, commonAddress, (ioa, [dco]));

    private static async Task<List<Asdu>> CollectUntilTerminationAsync(RawIec104Client client, int firstSendSequence)
    {
        var frames = new List<Asdu>();
        var send = firstSendSequence;
        while (true)
        {
            var apdu = await client.ReceiveApduAsync();
            if (apdu.Control.Kind != ApduFrameKind.Information)
                continue; // link supervision (TESTFR/S) may interleave
            Assert.Equal(send++, apdu.Control.SendSequence);
            var asdu = Asdu.TryParse(apdu.Payload);
            Assert.True(asdu is not null, "received I-frame payload is not a valid ASDU");
            frames.Add(asdu);
            if (asdu.Header.Cause == CauseOfTransmission.ActivationTermination)
                return frames;
        }
    }

    private static bool HasAudit(Iec104TestHost host, string fragment) =>
        host.Factory.Services.GetRequiredService<RecentEventBuffer>().GetRecent(500)
            .Any(e => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task StartDtStopDtAndSessionGating()
    {
        await using var host = await Iec104TestHost.StartAsync();
        // An I-frame before STARTDT closes the connection.
        await using var early = await RawIec104Client.ConnectAsync(host.Port);
        await early.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        Assert.True(await early.IsClosedAsync());
        Assert.True(HasAudit(host, "before STARTDT"));

        await using var client = await RawIec104Client.ConnectAsync(host.Port);
        await client.SendAsync(RawIec104Client.Unnumbered(Apci.StartDtAct));
        Assert.Equal(Apci.StartDtCon, (await client.ReceiveApduAsync()).Control.UnnumberedFunction);
        await client.SendAsync(RawIec104Client.Unnumbered(Apci.StopDtAct));
        Assert.Equal(Apci.StopDtCon, (await client.ReceiveApduAsync()).Control.UnnumberedFunction);
        // After STOPDT no data is accepted anymore.
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        Assert.True(await client.IsClosedAsync());
    }

    [Fact]
    public async Task GeneralInterrogationReturnsAllPointsOfTheCommonAddress()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var responses = await CollectUntilTerminationAsync(client, firstSendSequence: 0);

        Assert.Equal(Iec104TypeId.C_IC_NA_1, responses[0].Header.Type);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, responses[0].Header.Cause);
        Assert.Equal(20, responses[0].Objects[0].Data[0]);
        Assert.Equal(CauseOfTransmission.ActivationTermination, responses[^1].Header.Cause);

        var data = responses.Where(r => r.Header.Cause == CauseOfTransmission.InterrogationResponse).ToList();
        var objects = data.SelectMany(r => r.Objects).ToList();
        var expected = host.Factory.Services.GetRequiredService<Iec104AddressMap>().Rtu(1)!.Points;
        Assert.Equal(expected.Count, objects.Count);
        Assert.Equal(expected.Select(p => p.InformationObjectAddress).Order(), objects.Select(o => o.InformationObjectAddress).Order());
        foreach (var point in expected)
        {
            var carrier = data.Single(r => r.Objects.Any(o => o.InformationObjectAddress == point.InformationObjectAddress));
            var expectedType = point.Kind switch
            {
                Iec104PointKind.Measurement => Iec104TypeId.M_ME_NC_1,
                Iec104PointKind.SwitchStatus => Iec104TypeId.M_DP_NA_1,
                Iec104PointKind.TapPosition => Iec104TypeId.M_ST_NA_1,
                _ => Iec104TypeId.M_SP_TB_1
            };
            Assert.Equal(expectedType, carrier.Header.Type);
        }

        // Values come from the reported measurements and snapshot states.
        var current = objects.Single(o => o.InformationObjectAddress == 1000);
        Assert.Equal(120f, Iec104ElementCodec.DecodeMeasurement(current.Data));
        var voltage = objects.Single(o => o.InformationObjectAddress == 1005);
        Assert.Equal(10.2f, Iec104ElementCodec.DecodeMeasurement(voltage.Data));
        var switchPoint = objects.Single(o => o.InformationObjectAddress == 2000);
        Assert.Equal(Iec104ElementCodec.DoublePointClosed, Iec104ElementCodec.DecodeDoublePoint(switchPoint.Data[0]));
        var rtuStatus = objects.Single(o => o.InformationObjectAddress == 5000);
        Assert.Equal(Iec104TypeId.M_SP_TB_1, data.Single(r => r.Objects.Contains(rtuStatus)).Header.Type);
        Assert.Equal(1, Iec104ElementCodec.DecodeSinglePoint(rtuStatus.Data[0]));
    }

    [Fact]
    public async Task SpontaneousDataIsSentAfterAChangeBeyondTheDeadband()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);
        var monitor = host.Factory.Services.GetRequiredService<Iec104GridMonitor>();
        var t0 = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        // First step only primes the baseline.
        await monitor.OnGridStepAsync(new GridStepReport(t0, Iec104SampleGrid.CreateSnapshot(lineCurrentA: 120, switchClosed: true), []), CancellationToken.None);
        // Within the deadband (line range 400 A -> 2 A): no frame.
        await monitor.OnGridStepAsync(new GridStepReport(t0.AddSeconds(1), Iec104SampleGrid.CreateSnapshot(lineCurrentA: 121, switchClosed: true), []), CancellationToken.None);
        Assert.Null(await client.TryReceiveAsync(400));
        // Beyond the deadband plus a switch change: two spontaneous frames with COT 3 and CP56Time2a.
        await monitor.OnGridStepAsync(new GridStepReport(t0.AddSeconds(2), Iec104SampleGrid.CreateSnapshot(lineCurrentA: 130, switchClosed: false), []), CancellationToken.None);
        var (measurement, control) = await client.ReceiveAsduAsync();
        Assert.Equal(0, control.SendSequence);
        Assert.Equal(CauseOfTransmission.Spontaneous, measurement.Header.Cause);
        Assert.Equal(Iec104TypeId.M_ME_TF_1, measurement.Header.Type);
        Assert.Equal(1, measurement.Header.CommonAddress);
        Assert.Equal(1000, measurement.Objects[0].InformationObjectAddress);
        Assert.Equal(130f, Iec104ElementCodec.DecodeMeasurement(measurement.Objects[0].Data[..4]));
        Assert.Equal(t0.AddSeconds(2), Cp56Time2a.Decode(measurement.Objects[0].Data[5..]));
        var (switchFrame, _) = await client.ReceiveAsduAsync();
        Assert.Equal(Iec104TypeId.M_DP_TB_1, switchFrame.Header.Type);
        Assert.Equal(2000, switchFrame.Objects[0].InformationObjectAddress);
        Assert.Equal(Iec104ElementCodec.DoublePointOpen, Iec104ElementCodec.DecodeDoublePoint(switchFrame.Objects[0].Data[0]));
    }

    [Fact]
    public async Task SelectExecuteDoubleCommandReachesGridControl()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, DoubleCommand(1, 2000, 0x81))); // select open
        var (selectCon, selectControl) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirm, selectCon.Header.Cause);
        Assert.Equal(Iec104TypeId.C_DC_NA_1, selectCon.Header.Type);
        Assert.Equal(2000, selectCon.Objects[0].InformationObjectAddress);
        Assert.Equal(1, selectControl.ReceiveSequence); // V(R) advanced past the consumed I-frame

        await client.SendAsync(RawIec104Client.Information(1, 1, DoubleCommand(1, 2000, 0x01))); // execute open
        var responses = await CollectUntilTerminationAsync(client, firstSendSequence: 1);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, responses[0].Header.Cause);

        var commands = host.Control.Commands;
        Assert.Equal(2, commands.Count);
        Assert.Equal(GridCommandType.SelectOpen, commands[0].Command.Type);
        Assert.Equal(Iec104SampleGrid.SwitchId, commands[0].Command.TargetId);
        Assert.Equal(GridCommandType.Execute, commands[1].Command.Type);
        Assert.All(commands, c =>
        {
            Assert.Equal("iec104-master", c.Actor.Name);
            Assert.Equal(AccessRole.Operator, c.Actor.Role);
            Assert.Equal("iec104", c.Source);
        });
        Assert.True(HasAudit(host, "SelectOpen"));

        // A rejected command gets a negative activation confirmation and no termination.
        host.Control.Result = GridCommandResult.Reject(GridCommandRejection.Interlocked, "interlocked");
        await client.SendAsync(RawIec104Client.Information(2, 3, DoubleCommand(1, 2000, 0x82))); // select close
        var (rejected, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, rejected.Header.Cause);
        Assert.Null(await client.TryReceiveAsync(300));
        Assert.Equal(GridCommandType.SelectClose, host.Control.Commands[^1].Command.Type);
        Assert.True(HasAudit(host, "rejected"));
    }

    [Fact]
    public async Task RegulatingStepCommandMapsToTapRaiseAndLower()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0,
            RawIec104Client.AsduBytes(Iec104TypeId.C_RC_NA_1, CauseOfTransmission.ActivationRequest, 2, (3000, [0x01]))));
        var raise = await CollectUntilTerminationAsync(client, firstSendSequence: 0);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, raise[0].Header.Cause);
        await client.SendAsync(RawIec104Client.Information(1, 2,
            RawIec104Client.AsduBytes(Iec104TypeId.C_RC_NA_1, CauseOfTransmission.ActivationRequest, 2, (3000, [0x02]))));
        var lower = await CollectUntilTerminationAsync(client, firstSendSequence: 2);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, lower[0].Header.Cause);
        Assert.Equal([GridCommandType.TapRaise, GridCommandType.TapLower], host.Control.Commands.Select(c => c.Command.Type));
        Assert.All(host.Control.Commands, c => Assert.Equal(Iec104SampleGrid.TransformerId, c.Command.TargetId));
    }

    [Fact]
    public async Task UnknownIoaTypeCauseAndCommonAddressGetNegativeConfirms()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);

        // Unsupported interrogation group: negative activation confirmation.
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1, qoi: 21)));
        var (qoiCon, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, qoiCon.Header.Cause);

        // Unknown IOA on a command: COT 47.
        await client.SendAsync(RawIec104Client.Information(1, 1, DoubleCommand(1, 9999, 0x81)));
        var (ioaCon, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.UnknownInformationObjectAddress, ioaCon.Header.Cause);

        // Unknown type: COT 44 with the raw payload echoed.
        var unknownPayload = Asdu.Build(new AsduHeader((Iec104TypeId)200, false, 1, CauseOfTransmission.ActivationRequest, 0, 1),
            [new Iec104InformationObject(5, [9, 9, 9])]).Serialize();
        await client.SendAsync(RawIec104Client.Information(2, 2, unknownPayload));
        var (typeCon, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.UnknownTypeIdentification, typeCon.Header.Cause);
        Assert.Equal((Iec104TypeId)200, typeCon.Header.Type);

        // Unknown common address: COT 46.
        await client.SendAsync(RawIec104Client.Information(3, 3, Interrogation(99)));
        var (caCon, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.UnknownCommonAddress, caCon.Header.Cause);
        Assert.Equal(99, caCon.Header.CommonAddress);
    }

    [Fact]
    public async Task DisabledCommandsAreRejected()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:CommandsEnabled", "false"));
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, DoubleCommand(1, 2000, 0x81)));
        var (con, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, con.Header.Cause);
        Assert.Empty(host.Control.Commands);
        Assert.True(HasAudit(host, "commands are disabled"));
    }

    [Fact]
    public async Task DisallowedClientIsRejectedAndAudited()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:AllowedClients:0", "192.0.2.10"));
        await using var client = await RawIec104Client.ConnectAsync(host.Port);
        Assert.True(await client.IsClosedAsync());
        Assert.True(HasAudit(host, "not in AllowedClients"));
    }

    [Fact]
    public async Task MalformedFrameClosesConnectionAndIsAudited()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await RawIec104Client.ConnectAsync(host.Port);
        await client.SendAsync([0x67, 4, 0, 0, 0, 0]); // wrong start byte
        Assert.True(await client.IsClosedAsync());
        Assert.True(HasAudit(host, "Malformed APDU"));

        await using var second = await RawIec104Client.ConnectAsync(host.Port);
        await second.SendAsync([0x68, 3, 0, 0]); // length below the APCI size
        Assert.True(await second.IsClosedAsync());
    }

    [Fact]
    public async Task T3IdleSendsTestFramesAndTheLinkSurvivesTheAnswer()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:T3", "1"));
        await using var client = await StartedClientAsync(host.Port);
        var testFrame = await client.TryReceiveAsync(3000);
        Assert.NotNull(testFrame);
        var testApdu = Apdu.TryParse(testFrame!);
        Assert.Equal(ApduFrameKind.Unnumbered, testApdu!.Value.Control.Kind);
        Assert.Equal(Apci.TestFrAct, testApdu.Value.Control.UnnumberedFunction);
        await client.SendAsync(RawIec104Client.Unnumbered(Apci.TestFrCon));
        // The link still works after the test frame exchange.
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var responses = await CollectUntilTerminationAsync(client, firstSendSequence: 0);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, responses[0].Header.Cause);
    }

    [Fact]
    public async Task KWindowBlocksDataUntilTheMasterAcknowledges()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:K", "2"), ("Protocols:Iec104:W", "2"));
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var (first, c0) = await client.ReceiveAsduAsync();
        Assert.Equal(0, c0.SendSequence);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, first.Header.Cause);
        var (second, c1) = await client.ReceiveAsduAsync();
        Assert.Equal(1, c1.SendSequence);
        Assert.Equal(CauseOfTransmission.InterrogationResponse, second.Header.Cause);
        // k=2 reached: no further I-frame until an S-frame acknowledgement.
        Assert.Null(await client.TryReceiveAsync(400));
        await client.SendAsync(RawIec104Client.Supervisory(2));
        var (third, c2) = await client.ReceiveAsduAsync();
        Assert.Equal(2, c2.SendSequence);
        Assert.Equal(CauseOfTransmission.InterrogationResponse, third.Header.Cause);
        var (fourth, c3) = await client.ReceiveAsduAsync();
        Assert.Equal(3, c3.SendSequence);
        // The window is full again; only after the next acknowledgement does the termination arrive.
        Assert.Null(await client.TryReceiveAsync(400));
        await client.SendAsync(RawIec104Client.Supervisory(4));
        var (fifth, c4) = await client.ReceiveAsduAsync();
        Assert.Equal(4, c4.SendSequence);
        Assert.Equal(CauseOfTransmission.ActivationTermination, fifth.Header.Cause);
    }

    [Fact]
    public async Task WTriggersAnSAcknowledgementOfReceivedIframes()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:W", "1"));
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var informationFrames = 0;
        Apci? supervisory = null;
        for (var i = 0; i < 10 && supervisory is null; i++)
        {
            var apdu = await client.ReceiveApduAsync();
            if (apdu.Control.Kind == ApduFrameKind.Information)
                informationFrames++;
            else if (apdu.Control.Kind == ApduFrameKind.Supervisory)
                supervisory = apdu.Control;
        }

        Assert.True(informationFrames >= 3, "expected the interrogation responses as I-frames");
        Assert.NotNull(supervisory);
        Assert.Equal(1, supervisory!.Value.ReceiveSequence); // w=1: after the first received I-frame
    }

    [Fact]
    public async Task T1TimeoutClosesAnUnacknowledgedConnection()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:T1", "1"));
        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var (con, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirm, con.Header.Cause);
        // Never acknowledge the server I-frame: t1 must close the connection.
        Assert.True(await client.IsClosedAsync());
        Assert.True(HasAudit(host, "t1 timeout"));
    }

    [Fact]
    public async Task ConnectionLimitRejectsExcessClients()
    {
        await using var host = await Iec104TestHost.StartAsync(("Protocols:Iec104:MaxConnections", "1"));
        await using var first = await StartedClientAsync(host.Port);
        await using var second = await RawIec104Client.ConnectAsync(host.Port);
        Assert.True(await second.IsClosedAsync());
        Assert.Equal(1, host.Server.ActiveConnections);
        Assert.True(HasAudit(host, "connection limit"));
        // The first client still works.
        await first.SendAsync(RawIec104Client.Information(0, 0, Interrogation(1)));
        var responses = await CollectUntilTerminationAsync(first, firstSendSequence: 0);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, responses[0].Header.Cause);
    }

    [Fact]
    public async Task ClockSyncIsAcknowledgedWithoutChangingServerTime()
    {
        await using var host = await Iec104TestHost.StartAsync();
        await using var client = await StartedClientAsync(host.Port);
        var tag = Cp56Time2a.Encode(new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero));
        await client.SendAsync(RawIec104Client.Information(0, 0,
            RawIec104Client.AsduBytes(Iec104TypeId.C_CS_NA_1, CauseOfTransmission.ActivationRequest, 1, (0, tag))));
        var (con, _) = await client.ReceiveAsduAsync();
        Assert.Equal(Iec104TypeId.C_CS_NA_1, con.Header.Type);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, con.Header.Cause);
        Assert.Equal(tag, con.Objects[0].Data);
        Assert.True(HasAudit(host, "clock synchronisation"));
    }

    [Fact]
    public async Task MapEndpointReturnsTheAddressMapForViewers()
    {
        await using var host = await Iec104TestHost.StartAsync();
        using var http = host.Factory.Client(ApiTestFactory.ViewerKey);
        using var json = await http.GetFromJsonAsync<JsonDocument>("/api/grid/iec104/map");
        var rtus = json!.RootElement.GetProperty("rtus");
        Assert.Equal(2, rtus.GetArrayLength());
        var rtu1 = rtus[0];
        Assert.Equal(1, rtu1.GetProperty("commonAddress").GetInt32());
        var points = rtu1.GetProperty("points");
        Assert.Contains(points.EnumerateArray(), p =>
            p.GetProperty("informationObjectAddress").GetInt32() == 2000 && p.GetProperty("kind").GetString() == "SwitchStatus");
        Assert.Contains(points.EnumerateArray(), p =>
            p.GetProperty("informationObjectAddress").GetInt32() == 1000 && p.GetProperty("kind").GetString() == "Measurement");
        using var anonymous = host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/grid/iec104/map")).StatusCode);
    }
}
