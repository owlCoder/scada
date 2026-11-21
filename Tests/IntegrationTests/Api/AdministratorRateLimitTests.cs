using System.Net;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class AdministratorRateLimitTests
{
    [Fact]
    public async Task ScenarioCommandsShareTheCredentialRateLimit()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AdminKey);

        for (var i = 0; i < 5; i++)
        {
            using var start = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            using var stop = await client.PostAsync("/api/scenarios/active/stop", null);
            Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        }

        using var blocked = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task UnknownScenarioDoesNotConsumeRateLimit()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AdminKey);

        for (var i = 0; i < 15; i++)
        {
            using var response = await client.PostAsync("/api/scenarios/not-found", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var valid = await client.PostAsync("/api/scenarios/sensor-spoofing", null);
        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
    }

    [Fact]
    public async Task SecondStartWhileActiveIsAConflict()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.AdminKey);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scenarios/sensor-spoofing/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/scenarios/command-replay/start", null)).StatusCode);
    }
}
