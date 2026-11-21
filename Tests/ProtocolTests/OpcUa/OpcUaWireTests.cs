using Application.Simulation;
using Application.Training;
using Domain.Process.Devices;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;
using IntegrationTests.Support;
using Xunit;

namespace ProtocolTests.OpcUa;

public sealed class OpcUaWireTests
{
    [Fact]
    public async Task EntirePlantTreeIsReadableGoodAndContainsOnlyReportedMeasurements()
    {
        await using var host = await OpcUaTestHost.StartAsync();
        var services = host.Factory.Services;
        Assert.True(services.GetRequiredService<ITrainingEnvironment>().SpoofSensor("LT-101", 37).Accepted);
        await services.GetRequiredService<SimulationCoordinator>().StepManyAsync(1, CancellationToken.None);
        using var session = await host.ConnectAsync();
        var expectedFolders = new[] { "Controllers", "Pumps", "Sensors", "Tanks", "Valves" };
        var folders = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, "Plant"));
        Assert.Equal(expectedFolders, folders.Select(reference => reference.BrowseName.Name).Order());
        var snapshot = services.GetRequiredService<IPlantView>().Snapshot();
        var expectedDevices = new Dictionary<string, string[]>
        {
            ["Tanks"] = snapshot.Tanks.Select(device => device.Id).Order().ToArray(),
            ["Pumps"] = snapshot.Pumps.Select(device => device.Id).Order().ToArray(),
            ["Valves"] = snapshot.Valves.Select(device => device.Id).Order().ToArray(),
            ["Sensors"] = snapshot.Sensors.Select(device => device.Id).Order().ToArray(),
            ["Controllers"] = ["PLC-1", "PLC-2", "RTU-1"]
        };
        foreach (var folder in expectedFolders)
        {
            var devices = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, $"Plant/{folder}"));
            Assert.Equal(expectedDevices[folder], devices.Select(reference => reference.BrowseName.Name).Order());
        }

        var pending = new Queue<NodeId>();
        var visited = new HashSet<NodeId>();
        var variables = new List<NodeId>();
        pending.Enqueue(OpcUaTestHost.PlantNode(session, "Plant"));
        string[] forbidden = ["TrueValue", "IsSpoofed", "SpoofedValue", "DriftOffset", "Fault", "InterlocksBypassed"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (pending.TryDequeue(out var node))
        {
            if (!visited.Add(node))
                continue;
            foreach (var reference in await OpcUaTestHost.BrowseAsync(session, node))
            {
                Assert.DoesNotContain(reference.BrowseName.Name, forbidden);
                var child = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                Assert.NotNull(child);
                pending.Enqueue(child);
                if (reference.NodeClass != NodeClass.Variable)
                    continue;
                variables.Add(child);
                var value = await session.ReadValueAsync(child, timeout.Token);
                Assert.True(StatusCode.IsGood(value.StatusCode), $"{child}: {value.StatusCode}");
                Assert.NotNull(value.Value);
            }
        }
        Assert.True(variables.Count >= 100, $"Expected the full variable tree, got {variables.Count} variables.");
        // Check exact field allowlists, including the measurement redaction surface.
        foreach (var sensor in snapshot.Sensors)
        {
            var fields = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, $"Plant/Sensors/{sensor.Id}"));
            Assert.Equal(new[] { "Health", "Quality", "Unit", "Value" }, fields.Select(field => field.BrowseName.Name).Order());
        }
        Assert.NotEqual(37, snapshot.Sensor("LT-101")!.TrueValue);
        await WaitForValueAsync(session, "Plant/Tanks/T-101/Level", 37, timeout.Token);
        Assert.Equal(37, await OpcUaTestHost.ReadDoubleAsync(session, "Plant/Sensors/LT-101/Value"));
        Assert.Equal(snapshot.Tank("T-101")!.Capacity * .37,
            await OpcUaTestHost.ReadDoubleAsync(session, "Plant/Tanks/T-101/Volume"), 6);
        foreach (var field in forbidden)
        {
            var read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = OpcUaTestHost.PlantNode(session, $"Plant/Sensors/LT-101/{field}"), AttributeId = Attributes.Value }],
                timeout.Token);
            Assert.Equal(StatusCodes.BadNodeIdUnknown, Assert.Single(read.Results).StatusCode);
        }
    }

    [Fact]
    public async Task SubscriptionDeliversEachDeterministicallyDrivenChange()
    {
        await using var host = await OpcUaTestHost.StartAsync();
        using var session = await host.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var values = System.Threading.Channels.Channel.CreateUnbounded<double>();
        using var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 50 };
        var item = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = OpcUaTestHost.PlantNode(session, "Plant/Sensors/LT-101/Value"),
            AttributeId = Attributes.Value, SamplingInterval = 0, QueueSize = 16
        };
        item.Notification += (monitored, _) =>
        {
            foreach (var value in monitored.DequeueValues())
                if (StatusCode.IsGood(value.StatusCode) && value.Value is double number)
                    values.Writer.TryWrite(number);
        };
        subscription.AddItem(item);
        Assert.True(session.AddSubscription(subscription));
        await subscription.CreateAsync(timeout.Token);
        var initial = await values.Reader.ReadAsync(timeout.Token);
        Assert.Equal(host.Factory.Services.GetRequiredService<IPlantView>().Snapshot().Sensor("LT-101")!.ReportedValue, initial);
        var coordinator = host.Factory.Services.GetRequiredService<SimulationCoordinator>();
        foreach (var value in new[] { 31d, 62d, 24d })
        {
            Assert.True(host.Factory.Services.GetRequiredService<ITrainingEnvironment>().SpoofSensor("LT-101", value).Accepted);
            Assert.Equal(1, await coordinator.StepManyAsync(1, timeout.Token));
            double received;
            do
            {
                received = await values.Reader.ReadAsync(timeout.Token);
            } while (received != value);
            Assert.Equal(value, received);
        }
        Assert.Equal(3, host.Factory.Services.GetRequiredService<SimulationEngine>().Status.StepCount);
        await subscription.DeleteAsync(false, timeout.Token);
    }

    [Fact]
    public async Task MethodsEnforceRolesTypesRangesAndApplyOperatorCommands()
    {
        await using var host = await OpcUaTestHost.StartAsync();
        using var anonymous = await host.ConnectAsync();
        using var viewer = await host.ConnectAsync(MessageSecurityMode.SignAndEncrypt, "opc-viewer", OpcUaTestHost.ViewerPassword);
        const string pump = "Plant/Pumps/P-101";
        const string valve = "Plant/Valves/V-301";
        foreach (var denied in new[] { anonymous, viewer })
        {
            foreach (var method in new[] { "StartPump", "StopPump", "SetManual", "SetAutomatic", "ResetInterlock" })
                Assert.Equal(StatusCodes.BadUserAccessDenied, await OpcUaTestHost.CallAsync(denied, pump, method));
            Assert.Equal(StatusCodes.BadUserAccessDenied,
                await OpcUaTestHost.CallAsync(denied, valve, "SetValvePosition", new Variant(45d)));
        }
        using var user = await host.ConnectAsync(MessageSecurityMode.SignAndEncrypt, "ana", OpcUaTestHost.OperatorPassword);
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, valve, "SetManual"));
        foreach (var value in new[] { -1d, 101d, double.NaN, double.PositiveInfinity })
            Assert.Equal(StatusCodes.BadOutOfRange, await OpcUaTestHost.CallAsync(user, valve, "SetValvePosition", new Variant(value)));
        foreach (var argument in new[] { new Variant("45"), new Variant(45), new Variant(new[] { 45d }) })
            Assert.Equal(StatusCodes.BadInvalidArgument, await OpcUaTestHost.CallAsync(user, valve, "SetValvePosition", argument));
        Assert.Equal(StatusCodes.BadArgumentsMissing, await OpcUaTestHost.CallAsync(user, valve, "SetValvePosition"));
        Assert.Equal(StatusCodes.BadTooManyArguments,
            await OpcUaTestHost.CallAsync(user, valve, "SetValvePosition", new Variant(1d), new Variant(2d)));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, valve, "SetValvePosition", new Variant(45d)));
        var plant = host.Factory.Services.GetRequiredService<IPlantView>();
        Assert.Equal(45, plant.Snapshot().Valve("V-301")!.Setpoint);
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, pump, "SetManual"));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, pump, "StartPump"));
        Assert.Equal(PumpState.Starting, plant.Snapshot().Pump("P-101")!.State);
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, pump, "StopPump"));
        Assert.Equal(PumpState.Stopping, plant.Snapshot().Pump("P-101")!.State);
        await host.Factory.Services.GetRequiredService<SimulationCoordinator>().StepManyAsync(1, CancellationToken.None);
        Assert.Equal(PumpState.Stopped, plant.Snapshot().Pump("P-101")!.State);
    }

    [Fact]
    public async Task AnonymousDisabledRemovesNoneEndpointAndAnonymousTokens()
    {
        await using var host = await OpcUaTestHost.StartAsync(("Protocols:OpcUa:AllowAnonymous", "false"));
        var endpoints = await host.DiscoverAsync();
        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotEqual(MessageSecurityMode.None, endpoint.SecurityMode);
            Assert.NotEqual(SecurityPolicies.None, endpoint.SecurityPolicyUri);
            Assert.All(endpoint.UserIdentityTokens, token => Assert.Equal(UserTokenType.UserName, token.TokenType));
        });
        var exception = await Assert.ThrowsAsync<ServiceResultException>(
            () => host.ConnectAsync(MessageSecurityMode.SignAndEncrypt));
        Assert.Contains(exception.StatusCode, new[] { StatusCodes.BadIdentityTokenRejected, StatusCodes.BadIdentityTokenInvalid,
            StatusCodes.BadUserAccessDenied });
    }

    private static async Task<double> WaitForValueAsync(ISession session, string path, double expected, CancellationToken token)
    {
        while (true)
        {
            var value = await OpcUaTestHost.ReadDoubleAsync(session, path);
            if (value == expected)
                return value;
            await Task.Delay(10, token);
        }
    }
}
