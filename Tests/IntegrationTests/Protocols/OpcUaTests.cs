using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Application.Events;
using Application.Simulation;
using Application.Training;
using Domain.Process.Devices;
using IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace IntegrationTests.Protocols;

public sealed class OpcUaTests
{
    private const string TankLevel = "Plant/Tanks/T-101/Level";
    private const string Pump = "Plant/Pumps/P-101";
    private const string Valve = "Plant/Valves/V-301";

    [Fact]
    public async Task AnonymousSessionBrowsesAndReceivesRedactedChangesAfterSimulationSteps()
    {
        await using var host = await OpcUaTestHost.StartAsync();
        using var session = await host.ConnectAsync();
        var services = host.Factory.Services;
        var plantView = services.GetRequiredService<IPlantView>();
        var coordinator = services.GetRequiredService<SimulationCoordinator>();

        var objects = await OpcUaTestHost.BrowseAsync(session, ObjectIds.ObjectsFolder);
        var root = Assert.Single(objects, reference => reference.BrowseName.Name == "Plant");
        Assert.Equal(OpcUaTestHost.PlantNode(session, "Plant"), ExpandedNodeId.ToNodeId(root.NodeId, session.NamespaceUris));
        var folders = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, "Plant"));
        Assert.Equal(new[] { "Controllers", "Pumps", "Sensors", "Tanks", "Valves" },
            folders.Select(folder => folder.BrowseName.Name).Order());
        var tanks = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, "Plant/Tanks"));
        Assert.Contains(tanks, tank => tank.BrowseName.Name == "T-101" && tank.NodeClass == NodeClass.Object);
        var sensor = await OpcUaTestHost.BrowseAsync(session, OpcUaTestHost.PlantNode(session, "Plant/Sensors/LT-101"));
        Assert.Equal(new[] { "Health", "Quality", "Unit", "Value" },
            sensor.Select(reference => reference.BrowseName.Name).Order());

        var current = plantView.Snapshot().Tank("T-101")!.LevelPercent;
        Assert.Equal(current, await OpcUaTestHost.ReadDoubleAsync(session, TankLevel));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 100 };
        var initial = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = OpcUaTestHost.PlantNode(session, TankLevel),
            AttributeId = Attributes.Value,
            SamplingInterval = 0,
            QueueSize = 4
        };
        item.Notification += (monitored, _) =>
        {
            foreach (var value in monitored.DequeueValues())
            {
                if (value.Value is not double level || StatusCode.IsBad(value.StatusCode))
                    continue;
                if (level == current)
                    initial.TrySetResult(level);
                else
                    changed.TrySetResult(level);
            }
        };
        subscription.AddItem(item);
        Assert.True(session.AddSubscription(subscription));
        await subscription.CreateAsync(timeout.Token);
        Assert.Equal(current, await initial.Task.WaitAsync(timeout.Token));

        const double reported = 37;
        Assert.True(services.GetRequiredService<ITrainingEnvironment>().SpoofSensor("LT-101", reported).Accepted);
        Assert.Equal(1, await coordinator.StepManyAsync(1, timeout.Token));
        Assert.NotEqual(reported, plantView.Snapshot().Tank("T-101")!.LevelPercent);
        Assert.Equal(reported, await changed.Task.WaitAsync(timeout.Token));
        Assert.Equal(reported, await OpcUaTestHost.ReadDoubleAsync(session, "Plant/Sensors/LT-101/Value"));
        await subscription.DeleteAsync(silent: false, timeout.Token);
    }

    [Fact]
    public async Task OperatorCommandsChangeThePlantAndViewerCallsAreAudited()
    {
        await using var host = await OpcUaTestHost.StartAsync();
        var services = host.Factory.Services;
        var plant = services.GetRequiredService<IPlantView>();
        var events = services.GetRequiredService<RecentEventBuffer>();

        using var anonymous = await host.ConnectAsync();
        Assert.Equal(StatusCodes.BadUserAccessDenied, await OpcUaTestHost.CallAsync(anonymous, Pump, "StartPump"));
        using var viewer = await host.ConnectAsync(MessageSecurityMode.SignAndEncrypt, "opc-viewer", OpcUaTestHost.ViewerPassword);
        Assert.Equal(StatusCodes.BadUserAccessDenied,
            await OpcUaTestHost.CallAsync(viewer, Valve, "SetValvePosition", new Variant(45d)));
        Assert.Equal(PumpState.Stopped, plant.Snapshot().Pump("P-101")!.State);
        Assert.NotEqual(45, plant.Snapshot().Valve("V-301")!.Setpoint);

        using var user = await host.ConnectAsync(MessageSecurityMode.SignAndEncrypt, "ana", OpcUaTestHost.OperatorPassword);
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Pump, "SetManual"));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Pump, "StartPump"));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Valve, "SetManual"));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Valve, "SetValvePosition", new Variant(45d)));
        Assert.Equal(PumpState.Starting, plant.Snapshot().Pump("P-101")!.State);
        Assert.Equal(45, plant.Snapshot().Valve("V-301")!.Setpoint);
        Assert.Equal(StatusCodes.BadInvalidArgument, await OpcUaTestHost.CallAsync(user, Valve, "SetValvePosition", new Variant("45")));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Pump, "StopPump"));
        Assert.Equal(StatusCodes.Good, await OpcUaTestHost.CallAsync(user, Pump, "SetAutomatic"));

        Assert.Contains(events.GetRecent(), item => item is
            { Source: "OpcUa", Actor: "opcua-anonymous", DeviceId: "P-101", Category: EventCategories.AuthorizationDenied });
        Assert.Contains(events.GetRecent(), item => item is
            { Source: "OpcUa", Actor: "opc-viewer", DeviceId: "V-301", Category: EventCategories.AuthorizationDenied });
        Assert.Contains(events.GetRecent(), item => item is
            { Source: "OpcUa", Actor: "ana", DeviceId: "V-301", Category: EventCategories.Audit } &&
            item.Message.Contains("45%", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoveryAdvertisesOnlyConfiguredPoliciesAndHostReleasesPort()
    {
        int port;
        await using (var host = await OpcUaTestHost.StartAsync(("Protocols:OpcUa:AllowAnonymous", "false")))
        {
            port = host.Port;
            var listener = Assert.Single(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(),
                endpoint => endpoint.Port == port);
            Assert.Equal(IPAddress.Loopback, listener.Address);
            var endpoints = await host.DiscoverAsync();
            Assert.Equal(new[] { MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt },
                endpoints.Select(endpoint => endpoint.SecurityMode).Order());
            foreach (var endpoint in endpoints)
            {
                Assert.Equal("localhost", new Uri(endpoint.EndpointUrl).Host);
                Assert.Equal(SecurityPolicies.Basic256Sha256, endpoint.SecurityPolicyUri);
                Assert.Equal(UserTokenType.UserName, Assert.Single(endpoint.UserIdentityTokens).TokenType);
            }

            var signed = Assert.Single(endpoints, endpoint => endpoint.SecurityMode == MessageSecurityMode.Sign);
            await Assert.ThrowsAsync<ServiceResultException>(() => host.ConnectAsync(signed, "ana", "incorrect-password"));
            using var session = await host.ConnectAsync(signed, "ana", OpcUaTestHost.OperatorPassword);
            Assert.True(session.Connected);
            Assert.Single(Directory.GetFiles(Path.Combine(host.ServerPki, "own", "certs"), "*.der"));
            Assert.Single(Directory.GetFiles(Path.Combine(host.ServerPki, "own", "private"), "*.pfx"));
        }

        using var probe = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task UntrustedClientCertificateIsRejectedWhenAutoAcceptIsDisabled()
    {
        await using var host = await OpcUaTestHost.StartAsync(("Protocols:OpcUa:AutoAcceptUntrustedCertificates", "false"));
        var endpoints = await host.DiscoverAsync();
        var unsecured = Assert.Single(endpoints, endpoint => endpoint.SecurityMode == MessageSecurityMode.None);
        Assert.Equal(SecurityPolicies.None, unsecured.SecurityPolicyUri);
        Assert.Equal(UserTokenType.Anonymous, Assert.Single(unsecured.UserIdentityTokens).TokenType);
        var error = await Assert.ThrowsAsync<ServiceResultException>(
            () => host.ConnectAsync(MessageSecurityMode.SignAndEncrypt, "ana", OpcUaTestHost.OperatorPassword));
        Assert.Contains(error.StatusCode, new[] { StatusCodes.BadCertificateUntrusted, StatusCodes.BadSecurityChecksFailed });
    }
}
