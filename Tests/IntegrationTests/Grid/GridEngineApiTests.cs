using System.Net;
using System.Net.Http.Json;
using System.Text;
using Domain.Grid.Definition;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Grid;

public sealed class GridEngineApiTests
{
    [Theory]
    [InlineData("/api/grid")]
    [InlineData("/api/grid/status")]
    [InlineData("/api/grid/definition")]
    [InlineData("/api/grid/definitions")]
    [InlineData("/api/grid/measurements")]
    public async Task ReadEndpointsRequireAuthentication(string path)
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var anonymous = factory.Client(null);
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/grid/commands", "{\"type\":\"SelectOpen\",\"targetId\":\"QF1\"}")]
    [InlineData("/api/grid/runtime/pause", "{}")]
    [InlineData("/api/grid/runtime/resume", "{}")]
    public async Task ControlEndpointsRejectViewer(string path, string payload)
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(path, body)).StatusCode);
    }

    [Theory]
    [InlineData("/api/grid/runtime/reset", "{}")]
    [InlineData("/api/grid/definitions/1/activate", "{}")]
    [InlineData("/api/grid/definitions", "{}")]
    [InlineData("/api/grid/definitions/validate", "{}")]
    [InlineData("/api/grid/training", "{\"action\":\"fault\",\"targetId\":\"F1-1\",\"fault\":\"EarthFault\"}")]
    public async Task AdministrativeEndpointsRejectOperator(string path, string payload)
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var client = factory.Client(ApiTestFactory.OperatorKey);
        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path, body)).StatusCode);
    }

    [Fact]
    public async Task OperatorSboAndInstructorTrainingUseRedactedViews()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var op = factory.Client(ApiTestFactory.OperatorKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync("/api/grid/commands", new { type = "Execute", targetId = "QF1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsJsonAsync("/api/grid/commands", new { type = "SelectOpen", targetId = "QF1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsJsonAsync("/api/grid/commands", new { type = "Execute", targetId = "QF1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/grid/training", new { action = "fault", targetId = "F1-1", fault = "EarthFault" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/grid/training", new { action = "spoof", targetId = "RTU2:F1-1-MV:Voltage", value = 20 })).StatusCode);
        var truth = await admin.GetJsonAsync("/api/grid");
        var view = await op.GetJsonAsync("/api/grid");
        Assert.Equal("EarthFault", truth.GetProperty("branches")[0].GetProperty("fault").GetString());
        Assert.Equal("None", view.GetProperty("branches")[0].GetProperty("fault").GetString());
        var spoof = Assert.Single(view.GetProperty("measurements").EnumerateArray(), m => m.GetProperty("id").GetString() == "RTU2:F1-1-MV:Voltage");
        Assert.False(spoof.GetProperty("isSpoofed").GetBoolean());
        Assert.Equal(20, spoof.GetProperty("trueValue").GetDouble());
    }

    [Fact]
    public async Task DefinitionsValidateSaveAndActivateAndRuntimeResets()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var op = factory.Client(ApiTestFactory.OperatorKey);
        var d = DefaultGridDefinition.Create() with { Name = "REST edited grid" };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/grid/definitions/validate", d)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/grid/definitions", new { definition = d, comment = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/grid/definitions/2/activate", null)).StatusCode);
        Assert.Equal("REST edited grid", (await op.GetJsonAsync("/api/grid/status")).GetProperty("definitionName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsync("/api/grid/runtime/pause", null)).StatusCode);
        Assert.True((await op.GetJsonAsync("/api/grid/status")).GetProperty("paused").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsync("/api/grid/runtime/resume", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/grid/runtime/reset", null)).StatusCode);
    }

    [Fact]
    public async Task CookieCommandsRequireCsrf()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings(("Grid:Enabled", "false")));
        await using var browser = BrowserSession.Open(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SignInAsync("eva", BrowserSession.EvaPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, await browser.PostApiAsync("/api/grid/runtime/pause", null));
        Assert.Equal(HttpStatusCode.OK, await browser.PostApiAsync("/api/grid/runtime/pause", await browser.ApiTokenAsync()));
    }
}
