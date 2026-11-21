using Application.Events;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Microsoft.Extensions.DependencyInjection;
using ProtocolTests.Support;
using Protocols.Iec104;
using Xunit;

namespace ProtocolTests.Iec104;

/// <summary>IEC 60870-5-104 commands go through <c>GridControlService</c>, so a blocking dispatcher tag on the switch
/// or on the switch's element must produce a negative activation confirmation on the wire.</summary>
public sealed class DispatcherIec104TagTests
{
    private static byte[] DoubleCommand(int commonAddress, int ioa, byte dco) =>
        RawIec104Client.AsduBytes(Iec104TypeId.C_DC_NA_1, CauseOfTransmission.ActivationRequest, commonAddress, (ioa, [dco]));

    private static async Task<RawIec104Client> StartedClientAsync(int port)
    {
        var client = await RawIec104Client.ConnectAsync(port);
        await client.SendAsync(RawIec104Client.Unnumbered(Apci.StartDtAct));
        Assert.Equal(Apci.StartDtCon, (await client.ReceiveApduAsync()).Control.UnnumberedFunction);
        return client;
    }

    private static bool HasAudit(DispatcherIec104TagHost host, string fragment) =>
        host.Factory.Services.GetRequiredService<RecentEventBuffer>().GetRecent(500)
            .Any(e => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task TaggedSwitchGetsNegativeActivationConfirmOverIec104()
    {
        await using var host = await DispatcherIec104TagHost.StartAsync();
        var tagging = host.Factory.Services.GetRequiredService<IGridTagging>();
        var map = host.Factory.Services.GetRequiredService<Iec104AddressMapProvider>().Current;
        var point = map.Rtus.SelectMany(r => r.Points).Single(p => p.Kind == Iec104PointKind.SwitchStatus && p.ElementId == "QF1");
        var actor = new Actor("tag-test-operator", AccessRole.Operator);

        // Tag the line the breaker connects (element relation), not the switch itself.
        var placed = await tagging.PlaceTagAsync("F1-1", GridTagKind.DoNotOperate, "wire test tag", actor, CancellationToken.None);
        Assert.True(placed.Accepted);

        await using var client = await StartedClientAsync(host.Port);
        await client.SendAsync(RawIec104Client.Information(0, 0, DoubleCommand(point.CommonAddress, point.InformationObjectAddress, 0x81))); // select open
        var (rejected, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, rejected.Header.Cause);
        Assert.Equal(Iec104TypeId.C_DC_NA_1, rejected.Header.Type);
        Assert.Equal(point.InformationObjectAddress, rejected.Objects[0].InformationObjectAddress);
        Assert.Null(await client.TryReceiveAsync(300)); // no activation termination for a rejected command
        Assert.True(HasAudit(host, "Blocked by DoNotOperate tag"));
        Assert.False(host.Factory.Services.GetRequiredService<IGridView>().Snapshot().Switch("QF1")!.Selected);

        // Removing the tag lets the very same command through with a positive confirmation.
        var tag = Assert.Single(tagging.Tags);
        Assert.True((await tagging.RemoveTagAsync(tag.Id, actor, CancellationToken.None)).Accepted);
        await client.SendAsync(RawIec104Client.Information(1, 1, DoubleCommand(point.CommonAddress, point.InformationObjectAddress, 0x81)));
        var (accepted, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirm, accepted.Header.Cause);
        Assert.True(host.Factory.Services.GetRequiredService<IGridView>().Snapshot().Switch("QF1")!.Selected);

        // A tag on the switch itself blocks as well; an Information tag never does.
        Assert.True((await tagging.PlaceTagAsync("QF1", GridTagKind.Earthed, "earthed", actor, CancellationToken.None)).Accepted);
        await client.SendAsync(RawIec104Client.Information(2, 2, DoubleCommand(point.CommonAddress, point.InformationObjectAddress, 0x82))); // select close
        var (rejectedAgain, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, rejectedAgain.Header.Cause);
        Assert.True(HasAudit(host, "Blocked by Earthed tag"));
        Assert.True((await tagging.RemoveTagAsync(tagging.Tags.Single().Id, actor, CancellationToken.None)).Accepted);
        Assert.True((await tagging.PlaceTagAsync("QF1", GridTagKind.Information, "note only", actor, CancellationToken.None)).Accepted);
        await client.SendAsync(RawIec104Client.Information(3, 3, DoubleCommand(point.CommonAddress, point.InformationObjectAddress, 0x82)));
        var (infoAccepted, _) = await client.ReceiveAsduAsync();
        Assert.Equal(CauseOfTransmission.ActivationConfirm, infoAccepted.Header.Cause);
    }
}
