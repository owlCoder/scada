using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class EndpointAuthorizationTests
{
    [Theory]
    [InlineData("/api/plant")]
    [InlineData("/api/plant/devices/T-101")]
    [InlineData("/api/simulation")]
    [InlineData("/api/controllers")]
    [InlineData("/api/network")]
    [InlineData("/api/events")]
    [InlineData("/api/events/recent")]
    [InlineData("/api/telemetry")]
    [InlineData("/api/telemetry/tags")]
    [InlineData("/api/trends/LT-101")]
    [InlineData("/api/alarms")]
    [InlineData("/api/alarms/history")]
    [InlineData("/api/incidents")]
    [InlineData("/api/scenarios")]
    [InlineData("/api/scenarios/runs")]
    [InlineData("/api/detections")]
    [InlineData("/api/detections/rules")]
    [InlineData("/api/detections/metrics")]
    [InlineData("/api/registers")]
    [InlineData("/api/registers/1/holding/100?count=4")]
    [InlineData("/api/me")]
    public async Task ReadEndpointsRequireViewerOrHigher(string path)
    {
        using var factory = ApiTestFactory.Create();
        using var anonymous = factory.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/simulation/step", "{\"count\":1}")]
    [InlineData("/api/simulation/pause", "{}")]
    [InlineData("/api/commands", "{\"deviceId\":\"P-101\",\"type\":\"StartPump\"}")]
    [InlineData("/api/plant/pump/P-101/command", "{\"running\":true,\"actor\":\"test\"}")]
    [InlineData("/api/plant/valve/V-101/command", "{\"open\":false,\"actor\":\"test\"}")]
    [InlineData("/api/plant/devices/P-101/mode/manual", "{}")]
    [InlineData("/api/registers/1/holding/100", "{\"value\":0}")]
    [InlineData("/api/alarms/any/acknowledge", "{}")]
    public async Task CommandsRejectViewer(string path, string payload)
    {
        using var factory = ApiTestFactory.Create();
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var body = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.PostAsync(path, body)).StatusCode);
    }

    [Theory]
    [InlineData("/api/simulation/reset")]
    [InlineData("/api/controllers/PLC-1/reset")]
    [InlineData("/api/network/links/L-PLC1-IO/disconnect")]
    [InlineData("/api/scenarios/reset")]
    [InlineData("/api/accounts/ana/password/rotate")]
    public async Task AdministrativeEndpointsRejectOperator(string path)
    {
        using var factory = ApiTestFactory.Create();
        using var operatorClient = factory.Client(ApiTestFactory.OperatorKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await operatorClient.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await operatorClient.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanExecuteScenarioAndOperatorCannot()
    {
        using var factory = ApiTestFactory.Create();
        using var operatorClient = factory.Client(ApiTestFactory.OperatorKey);
        using var administrator = factory.Client(ApiTestFactory.AdminKey);
        const string path = "/api/scenarios/sensor-spoofing";
        Assert.Equal(HttpStatusCode.Unauthorized, (await operatorClient.PostAsJsonAsync(path, new { actor = "operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await administrator.PostAsJsonAsync(path, new { actor = "admin" })).StatusCode);
    }
}
