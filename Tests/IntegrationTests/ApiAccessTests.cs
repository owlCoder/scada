using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntegrationTests;

public sealed class ApiAccessTests
{
    [Fact]
    public async Task HealthIsPublicButPlantRequiresKey()
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/plant")).StatusCode);
    }

    [Fact]
    public async Task ResponsesHaveSecurityHeaders()
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ViewerCanReadButCannotControlPump()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "viewer-integration-key");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/plant")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/plant/pump/P-101/command",
            new { running = true, actor = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OperatorCanControlValveButCannotStartScenario()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "operator-integration-key");
        var command = await client.PostAsJsonAsync("/api/plant/valve/V-101/command",
            new { open = false, actor = "test" });
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);

        var scenario = await client.PostAsJsonAsync("/api/scenarios/sensor-spoofing",
            new { actor = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, scenario.StatusCode);
    }

    [Fact]
    public async Task RateLimitCannotBeBypassedByChangingActor()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "operator-integration-key");
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/plant/tick",
                new { seconds = 1 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/plant/pump/P-101/command",
            new { running = true, actor = "different-actor" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task InvalidKeyCannotReadEvents()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "invalid-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task NamedUserCanReadOwnIdentity()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "ana-integration-key");
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me");
        Assert.Equal("ana", me.GetProperty("username").GetString());
        Assert.Equal("Operator", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task NamedOperatorAuditUsesVerifiedIdentity()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "ana-integration-key");
        var command = await client.PostAsJsonAsync("/api/plant/valve/V-101/command",
            new { open = false, actor = "forged-actor" });
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);

        var events = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/events");
        Assert.Contains(events.EnumerateArray(), item =>
            item.GetProperty("category").GetString() == "Audit" &&
            item.GetProperty("actor").GetString() == "ana");
    }

    [Fact]
    public async Task AdministratorCanStartScenario()
    {
        using var factory = new ApiTestFactory();
        using var client = Client(factory, "admin-integration-key");
        var response = await client.PostAsJsonAsync("/api/scenarios/sensor-spoofing",
            new { actor = "test" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static HttpClient Client(ApiTestFactory factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

}
