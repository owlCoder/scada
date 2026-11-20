using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IntegrationTests;

public sealed class AdministratorRateLimitTests
{
    [Fact]
    public async Task ScenarioCommandsShareTheCredentialRateLimit()
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "admin-integration-key");

        for (var i = 0; i < 10; i++)
        {
            using var response = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using var blocked = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task UnknownScenarioDoesNotConsumeRateLimit()
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "admin-integration-key");

        for (var i = 0; i < 15; i++)
        {
            using var response = await client.PostAsync("/api/scenarios/not-found", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var valid = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
    }
}
