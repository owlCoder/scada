using System.Net;
using Xunit;

namespace IntegrationTests;

public sealed class ReadValidationTests
{
    [Theory]
    [InlineData("/api/telemetry?count=0")]
    [InlineData("/api/telemetry?count=-1")]
    [InlineData("/api/telemetry?count=3601")]
    public async Task InvalidTelemetrySampleCountIsRejected(string path)
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "viewer-integration-key");

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/registers/-1")]
    [InlineData("/api/registers/65536")]
    [InlineData("/api/registers/99")]
    public async Task UnknownRegisterAddressesReturnNotFound(string path)
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "viewer-integration-key");

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ViewerCanReadDefaultTelemetryWindow()
    {
        using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "viewer-integration-key");

        using var response = await client.GetAsync("/api/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
