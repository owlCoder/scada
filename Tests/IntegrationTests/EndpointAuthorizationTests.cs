using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IntegrationTests;

public sealed class EndpointAuthorizationTests
{
    [Theory]
    [InlineData("/api/plant")]
    [InlineData("/api/events")]
    [InlineData("/api/telemetry")]
    [InlineData("/api/incidents")]
    [InlineData("/api/scenarios")]
    [InlineData("/api/registers")]
    [InlineData("/api/me")]
    public async Task ReadEndpointsRequireViewerOrHigher(string path)
    {
        using var factory = new ApiTestFactory();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var viewer = CreateClient(factory, "viewer-integration-key");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/plant/tick", "{\"seconds\":1}")]
    [InlineData("/api/plant/pump/P-101/command", "{\"running\":true,\"actor\":\"test\"}")]
    [InlineData("/api/plant/valve/V-101/command", "{\"open\":false,\"actor\":\"test\"}")]
    [InlineData("/api/registers/2", "{\"value\":0,\"actor\":\"test\"}")]
    public async Task CommandsRejectViewer(string path, string payload)
    {
        using var factory = new ApiTestFactory();
        using var viewer = CreateClient(factory, "viewer-integration-key");
        using var body = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.PostAsync(path, body)).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanExecuteScenarioAndOperatorCannot()
    {
        using var factory = new ApiTestFactory();
        using var operatorClient = CreateClient(factory, "operator-integration-key");
        using var administrator = CreateClient(factory, "admin-integration-key");
        const string path = "/api/scenarios/sensor-spoofing";
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await operatorClient.PostAsJsonAsync(path, new { actor = "operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await administrator.PostAsJsonAsync(path, new { actor = "admin" })).StatusCode);
    }

    private static HttpClient CreateClient(ApiTestFactory factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }
}
