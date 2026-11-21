using System.Net;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class ReadValidationTests
{
    [Theory]
    [InlineData("/api/telemetry?count=0")]
    [InlineData("/api/telemetry?count=-1")]
    [InlineData("/api/telemetry?count=3601")]
    [InlineData("/api/trends/LT-101?count=0")]
    [InlineData("/api/registers/1/coils/0")]
    [InlineData("/api/registers/1/holding/0?count=126")]
    public async Task InvalidQueriesAreRejected(string path)
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.ViewerKey);

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/registers/1/holding/-1")]
    [InlineData("/api/registers/1/holding/65536")]
    [InlineData("/api/registers/1/holding/250?count=10")]
    [InlineData("/api/registers/9/holding/0")]
    [InlineData("/api/plant/devices/X-999")]
    [InlineData("/api/scenarios/runs/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/incidents/00000000-0000-0000-0000-000000000001")]
    public async Task UnknownResourcesReturnNotFound(string path)
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.ViewerKey);

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ViewerCanReadTelemetryOnceTheSimulationHasRun()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.ViewerKey);

        var samples = await ApiTestFactory.EventuallyAsync(() => client.GetJsonAsync("/api/telemetry?tagId=LT-101&count=5"),
            s => s.GetArrayLength() > 0);
        Assert.InRange(samples.GetArrayLength(), 1, 5);
        Assert.Equal("LT-101", samples[0].GetProperty("tagId").GetString());
    }
}
