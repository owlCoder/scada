using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Application.Realtime;
using Application.Simulation;
using Application.Training;
using IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ProtocolTests.Support;
using Xunit;

namespace ProtocolTests.Realtime;

public sealed class ScadaHubWireTests
{
    [Fact]
    public async Task UnauthenticatedNegotiationReturns401()
    {
        await using var host = HubTestClient.CreateHost();
        await using var client = HubTestClient.Connect(host, null);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Theory]
    [InlineData(ApiTestFactory.ViewerKey, false)]
    [InlineData(ApiTestFactory.OperatorKey, false)]
    [InlineData(ApiTestFactory.AdminKey, true)]
    public async Task SnapshotAndRealtimeRespectRoleAndReportedValues(string key, bool instructor)
    {
        await using var host = HubTestClient.CreateHost();
        var services = host.Services;
        Assert.True(services.GetRequiredService<ITrainingEnvironment>().SpoofSensor("LT-101", 97).Accepted);
        await using var client = HubTestClient.Connect(host, key);
        var messages = Channel.CreateUnbounded<JsonElement>();
        client.On<string, JsonElement>("Realtime", (topic, payload) =>
        {
            if (topic == RealtimeTopics.Plant)
                messages.Writer.TryWrite(payload);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.StartAsync(timeout.Token);
        var snapshot = await client.InvokeAsync<JsonElement>("GetSnapshot", timeout.Token);
        Assert.Equal(3, snapshot.GetProperty("plant").GetProperty("tanks").GetArrayLength());
        AssertView(snapshot.GetProperty("plant"), instructor);
        var report = await services.GetRequiredService<SimulationCoordinator>().StepAsync(true, timeout.Token);
        Assert.NotNull(report);
        var live = await messages.Reader.ReadAsync(timeout.Token);
        AssertView(live, instructor);
        Assert.Equal(report.Plant.Snapshot.StepCount, live.GetProperty("stepCount").GetInt64());
    }

    [Fact]
    public async Task StalledClientTransportDoesNotBlockAnotherClientsMessagesOrPublisher()
    {
        await using var host = HubTestClient.CreateHost();
        StalledPollHandler? stalled = null;
        await using var slow = HubTestClient.Connect(host, ApiTestFactory.ViewerKey,
            inner => stalled = new StalledPollHandler(inner));
        await using var fast = HubTestClient.Connect(host, ApiTestFactory.ViewerKey);
        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fast.On<string, JsonElement>("Realtime", (topic, payload) =>
        {
            if (topic == RealtimeTopics.Simulation && payload.GetProperty("sequence").GetInt32() == 200)
                received.TrySetResult(200);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await slow.StartAsync(timeout.Token);
            await fast.StartAsync(timeout.Token);
            Assert.NotNull(stalled);
            await stalled.Blocked.WaitAsync(timeout.Token);
            var publisher = host.Services.GetRequiredService<IRealtimePublisher>();
            var padding = new string('x', 8192);
            for (var sequence = 1; sequence <= 200; sequence++)
                Assert.True(publisher.Publish(RealtimeTopics.Simulation, new { sequence, padding }));
            Assert.Equal(200, await received.Task.WaitAsync(timeout.Token));
            Assert.True((await fast.InvokeAsync<JsonElement>("GetSnapshot", timeout.Token)).TryGetProperty("plant", out _));
        }
        finally
        {
            stalled?.Release();
        }
    }

    [Fact]
    public async Task DisconnectCancelsPendingSendAndReleasesSubscriber()
    {
        await using var host = HubTestClient.CreateHost();
        StalledPollHandler? stalled = null;
        await using var client = HubTestClient.Connect(host, ApiTestFactory.ViewerKey,
            inner => stalled = new StalledPollHandler(inner));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.StartAsync(timeout.Token);
            Assert.NotNull(stalled);
            await stalled.Blocked.WaitAsync(timeout.Token);
            var feed = host.Services.GetRequiredService<IRealtimeFeed>();
            Assert.Equal(1, feed.SubscriberCount);
            var publisher = host.Services.GetRequiredService<IRealtimePublisher>();
            for (var sequence = 0; sequence < 200; sequence++)
                Assert.True(publisher.Publish(RealtimeTopics.Simulation, new string('x', 8192)));
            await client.StopAsync(timeout.Token);
            while (feed.SubscriberCount != 0)
                await Task.Delay(10, timeout.Token);
            Assert.Equal(0, feed.SubscriberCount);
        }
        finally
        {
            stalled?.Release();
        }
    }

    private static bool AssertView(JsonElement plant, bool instructor)
    {
        var sensor = HubTestClient.Sensor(plant);
        Assert.Equal(97, sensor.GetProperty("reportedValue").GetDouble());
        Assert.Equal(instructor, sensor.GetProperty("isSpoofed").GetBoolean());
        var tank = plant.GetProperty("tanks").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "T-101");
        if (instructor)
        {
            Assert.NotEqual(97, sensor.GetProperty("trueValue").GetDouble());
            Assert.Equal(97, sensor.GetProperty("spoofedValue").GetDouble());
            Assert.NotEqual(97, tank.GetProperty("levelPercent").GetDouble());
        }
        else
        {
            Assert.Equal(97, sensor.GetProperty("trueValue").GetDouble());
            Assert.Equal(JsonValueKind.Null, sensor.GetProperty("spoofedValue").ValueKind);
            Assert.Equal(0, sensor.GetProperty("fault").GetInt32());
            Assert.Equal(0, sensor.GetProperty("driftOffset").GetDouble());
            Assert.Equal(97, tank.GetProperty("levelPercent").GetDouble());
        }
        return true;
    }
}
