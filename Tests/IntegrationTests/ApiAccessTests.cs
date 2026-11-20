using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntegrationTests;

public sealed class ApiAccessTests : IClassFixture<ApiAccessTests.Factory>
{
    private readonly Factory _factory;

    public ApiAccessTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task HealthIsPublicButPlantRequiresKey()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/plant")).StatusCode);
    }

    [Fact]
    public async Task ViewerCanReadButCannotControlPump()
    {
        using var client = Client("viewer-integration-key");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/plant")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/plant/pump/P-101/command",
            new { running = true, actor = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OperatorCanControlValveButCannotStartScenario()
    {
        using var client = Client("operator-integration-key");
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
        using var client = Client("operator-integration-key");
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
    public async Task AdministratorCanStartScenario()
    {
        using var client = Client("admin-integration-key");
        var response = await client.PostAsJsonAsync("/api/scenarios/sensor-spoofing",
            new { actor = "test" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpClient Client(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:Provider"] = "Memory",
                    ["Protocols:ModbusEnabled"] = "false",
                    ["Security:ViewerApiKey"] = "viewer-integration-key",
                    ["Security:OperatorApiKey"] = "operator-integration-key",
                    ["Security:AdminApiKey"] = "admin-integration-key",
                    ["Security:CommandApiKey"] = ""
                });
            });
        }
    }
}
