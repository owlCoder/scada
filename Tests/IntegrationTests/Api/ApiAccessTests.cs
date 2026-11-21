using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class ApiAccessTests
{
    [Fact]
    public async Task HealthIsPublicButPlantRequiresKey()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/plant")).StatusCode);
    }

    [Fact]
    public async Task ResponsesHaveSecurityHeaders()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(null);
        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(response.Headers.CacheControl is { NoStore: true, NoCache: true });
    }

    [Fact]
    public async Task ViewerCanReadButCannotControlPump()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.ViewerKey);
        var plant = await client.GetJsonAsync("/api/plant");
        Assert.Equal(3, plant.GetProperty("tanks").GetArrayLength());

        var response = await client.PostAsJsonAsync("/api/plant/pump/P-101/command", new { running = true, actor = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OperatorCanControlValveButCannotStartScenario()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.OperatorKey);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/plant/devices/V-101/mode/manual", null)).StatusCode);
        var command = await client.PostAsJsonAsync("/api/plant/valve/V-101/command", new { open = false, actor = "test" });
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);
        var valve = await client.GetJsonAsync("/api/plant/devices/V-101");
        Assert.Equal(0, valve.GetProperty("setpoint").GetDouble());

        var scenario = await client.PostAsJsonAsync("/api/scenarios/sensor-spoofing", new { actor = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, scenario.StatusCode);
    }

    [Fact]
    public async Task CommandRejectedByThePlantReturnsConflictWithTheReason()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.OperatorKey);

        var response = await client.PostAsJsonAsync("/api/plant/pump/P-101/command", new { running = true });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ModeMismatch", body.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/plant/pump/P-999/command", new { running = true })).StatusCode);
    }

    [Fact]
    public async Task RateLimitCannotBeBypassedByChangingActor()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.OperatorKey);
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/simulation/step", new { count = 1 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/plant/pump/P-101/command", new { running = true, actor = "different-actor" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task InvalidKeyCannotReadEvents()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client("invalid-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task NamedUserCanReadOwnIdentity()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AnaKey);
        var me = await client.GetJsonAsync("/api/me");
        Assert.Equal("ana", me.GetProperty("username").GetString());
        Assert.Equal("Operator", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task NamedOperatorAuditUsesVerifiedIdentity()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AnaKey);
        await client.PostAsync("/api/plant/devices/V-101/mode/manual", null);
        var command = await client.PostAsJsonAsync("/api/plant/valve/V-101/command", new { open = false, actor = "forged-actor" });
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);

        var events = await ApiTestFactory.EventuallyAsync(() => client.GetJsonAsync("/api/events?category=Audit&deviceId=V-101"),
            e => e.GetProperty("totalCount").GetInt32() >= 2);
        var items = events.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("actor").GetString() == "ana");
        Assert.DoesNotContain(items, item => item.GetProperty("actor").GetString() == "forged-actor");
    }

    [Fact]
    public async Task AdministratorCanStartScenario()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AdminKey);
        var response = await client.PostAsJsonAsync("/api/scenarios/sensor-spoofing", new { actor = "test" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.StartsWith("/api/scenarios/runs/", response.Headers.Location!.OriginalString);
    }
}
