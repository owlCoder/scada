using IntegrationTests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IntegrationTests.Api;

public sealed class AuthenticatedAuditTests
{
    [Fact]
    public async Task NamedUserCanSendCommandsWithoutClientActor()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "ana-integration-key");

        var response = await client.PostAsJsonAsync("/api/plant/valve/V-101/command",
            new { open = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await client.GetFromJsonAsync<JsonElement>("/api/events");
        Assert.Contains(events.EnumerateArray(), item =>
            item.GetProperty("category").GetString() == "Audit" &&
            item.GetProperty("actor").GetString() == "ana");
    }

    [Fact]
    public async Task AdministratorCanStartScenarioWithoutClientActor()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "admin-integration-key");

        var response = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
