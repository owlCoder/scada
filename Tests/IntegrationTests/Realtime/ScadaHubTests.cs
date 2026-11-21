using System.Text.Json;
using System.Threading.Channels;
using Application.Training;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.Realtime;

public sealed class ScadaHubTests
{
    [Fact]
    public async Task UnauthenticatedClientsCannotConnect()
    {
        using var factory = ApiTestFactory.Create();
        await using var connection = Connect(factory, key: null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task ClientsGetAnInitialSnapshotAndLivePlantMessages()
    {
        using var factory = ApiTestFactory.Create();
        await using var connection = Connect(factory, ApiTestFactory.ViewerKey);
        var plantMessages = Channel.CreateUnbounded<JsonElement>();
        connection.On<string, JsonElement>("Realtime", (topic, payload) =>
        {
            if (topic == "plant")
                plantMessages.Writer.TryWrite(payload);
        });
        await connection.StartAsync();

        var snapshot = await connection.InvokeAsync<JsonElement>("GetSnapshot");
        Assert.Equal(3, snapshot.GetProperty("plant").GetProperty("tanks").GetArrayLength());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var live = await plantMessages.Reader.ReadAsync(timeout.Token);
        Assert.True(live.GetProperty("tanks").GetArrayLength() > 0);
    }

    [Fact]
    public async Task PlantDataIsRedactedForOperatorsButNotForInstructors()
    {
        using var factory = ApiTestFactory.Create();
        var training = factory.Services.GetRequiredService<ITrainingEnvironment>();
        Assert.True(training.SpoofSensor("LT-101", 97).Accepted);
        await using var viewer = Connect(factory, ApiTestFactory.ViewerKey);
        await using var admin = Connect(factory, ApiTestFactory.AdminKey);
        var viewerLevels = Channel.CreateUnbounded<double>();
        var adminLevels = Channel.CreateUnbounded<double>();
        viewer.On<string, JsonElement>("Realtime", (topic, payload) => Collect(topic, payload, viewerLevels));
        admin.On<string, JsonElement>("Realtime", (topic, payload) => Collect(topic, payload, adminLevels));
        await viewer.StartAsync();
        await admin.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(97, await viewerLevels.Reader.ReadAsync(timeout.Token), 1);
        Assert.NotEqual(97, await adminLevels.Reader.ReadAsync(timeout.Token), 1);
        var viewerSnapshot = await viewer.InvokeAsync<JsonElement>("GetSnapshot");
        Assert.Equal(97, Level(viewerSnapshot.GetProperty("plant")), 1);
    }

    [Fact]
    public async Task InjectedActuatorFaultsAndBypassesAreRedactedInSnapshotsAndLiveMessages()
    {
        using var factory = ApiTestFactory.Create(("Simulation:Host:RunLoop", "false"));
        var training = factory.Services.GetRequiredService<ITrainingEnvironment>();
        training.InjectFault(new Domain.Process.Commands.FaultInjection("P-101", PumpFault: Domain.Process.Devices.PumpFault.Seized));
        training.SetInterlockBypass(true, "scenario");
        await using var op = Connect(factory, ApiTestFactory.OperatorKey);
        await using var admin = Connect(factory, ApiTestFactory.AdminKey);
        var opMessages = Channel.CreateUnbounded<JsonElement>();
        var adminMessages = Channel.CreateUnbounded<JsonElement>();
        op.On<string, JsonElement>("Realtime", (topic, payload) => { if (topic == "plant") opMessages.Writer.TryWrite(payload); });
        admin.On<string, JsonElement>("Realtime", (topic, payload) => { if (topic == "plant") adminMessages.Writer.TryWrite(payload); });
        await op.StartAsync();
        await admin.StartAsync();
        var opSnapshot = (await op.InvokeAsync<JsonElement>("GetSnapshot")).GetProperty("plant");
        var adminSnapshot = (await admin.InvokeAsync<JsonElement>("GetSnapshot")).GetProperty("plant");
        Assert.False(opSnapshot.GetProperty("interlocksBypassed").GetBoolean());
        Assert.Equal((int)Domain.Process.Devices.PumpFault.None, opSnapshot.GetProperty("pumps")[0].GetProperty("fault").GetInt32());
        Assert.True(adminSnapshot.GetProperty("interlocksBypassed").GetBoolean());
        Assert.Equal((int)Domain.Process.Devices.PumpFault.Seized, adminSnapshot.GetProperty("pumps")[0].GetProperty("fault").GetInt32());
        factory.Services.GetRequiredService<Application.Realtime.IRealtimePublisher>().Publish("plant", training.Snapshot());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opLive = await opMessages.Reader.ReadAsync(timeout.Token);
        var adminLive = await adminMessages.Reader.ReadAsync(timeout.Token);
        Assert.False(opLive.GetProperty("interlocksBypassed").GetBoolean());
        Assert.Equal((int)Domain.Process.Devices.PumpFault.None, opLive.GetProperty("pumps")[0].GetProperty("fault").GetInt32());
        Assert.True(adminLive.GetProperty("interlocksBypassed").GetBoolean());
        Assert.Equal((int)Domain.Process.Devices.PumpFault.Seized, adminLive.GetProperty("pumps")[0].GetProperty("fault").GetInt32());
    }

    private static bool Collect(string topic, JsonElement payload, Channel<double> levels) =>
        topic == "plant" && levels.Writer.TryWrite(Level(payload));

    private static double Level(JsonElement plant) =>
        plant.GetProperty("tanks").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "T-101").GetProperty("levelPercent").GetDouble();

    private static HubConnection Connect(WebApplicationFactory<Program> factory, string? key)
    {
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/scada"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (key is not null)
                    options.Headers.Add("X-Api-Key", key);
            })
            .Build();
    }
}
