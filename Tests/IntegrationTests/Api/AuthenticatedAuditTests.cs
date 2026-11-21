using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class AuthenticatedAuditTests
{
    [Fact]
    public async Task NamedUserCanSendCommandsWithoutClientActor()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AnaKey);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/plant/devices/V-301/mode/manual", null)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/plant/valve/V-301/command", new { position = 40 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await ApiTestFactory.EventuallyAsync(() => client.GetJsonAsync("/api/events?actor=ana&search=V-301"),
            e => e.GetProperty("totalCount").GetInt32() >= 2);
        Assert.Contains(events.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("category").GetString() == "Audit" && item.GetProperty("message").GetString()!.StartsWith("Set V-301 to 40%"));
    }

    [Fact]
    public async Task AdministratorCanStartScenarioWithoutClientActor()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AdminKey);

        var response = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var current = await client.GetJsonAsync("/api/scenarios/current");
        Assert.Equal("legacy-Administrator", current.GetProperty("startedBy").GetString());
    }
}
