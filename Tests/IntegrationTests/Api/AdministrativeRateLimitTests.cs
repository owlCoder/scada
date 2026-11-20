using IntegrationTests.Support;
using System.Net;
using Xunit;

namespace IntegrationTests.Api;

public sealed class AdministrativeRateLimitTests
{
    [Fact]
    public async Task AdministratorScenarioRequestsAreLimitedPerCredential()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "admin-integration-key");

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var blocked = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task UnknownScenarioDoesNotConsumeRateLimit()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "admin-integration-key");

        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsync("/api/scenarios/unknown", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var valid = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
    }
}
