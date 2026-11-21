using System.Net;
using System.Net.Http.Json;
using System.Text;
using Application.Grid;
using IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.Grid;

public sealed class GridDispatcherApiTests
{
    [Theory]
    [InlineData("/api/grid/tags")]
    [InlineData("/api/grid/history/RTU1:HV:Voltage?points=5")]
    public async Task ReadEndpointsRequireAuthenticationAndAllowViewer(string path)
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var anonymous = factory.Client(null);
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task TagWritesRejectViewer()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        var body = new StringContent("{\"elementId\":\"QF1\",\"kind\":\"DoNotOperate\",\"reason\":\"test\"}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/grid/tags", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync("/api/grid/tags/abc")).StatusCode);
    }

    [Fact]
    public async Task OperatorCanPlaceListAndRemoveTagsOverRest()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var op = factory.Client(ApiTestFactory.OperatorKey);
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);

        var placed = await op.PostAsJsonAsync("/api/grid/tags", new { elementId = "QF1", kind = "DoNotOperate", reason = "rest tag test" });
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var result = await placed.Content.ReadFromJsonAsync<GridCommandResultDto>();
        Assert.NotNull(result);
        Assert.True(result.Accepted);

        var tags = await viewer.GetFromJsonAsync<List<TagDto>>("/api/grid/tags");
        var tag = Assert.Single(tags!);
        Assert.Equal("QF1", tag.ElementId);
        Assert.Equal("DoNotOperate", tag.Kind);
        Assert.Equal("rest tag test", tag.Reason);

        // Placing on an unknown element or without a reason is a 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await op.PostAsJsonAsync("/api/grid/tags", new { elementId = "NOPE", kind = "Earthed", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await op.PostAsJsonAsync("/api/grid/tags", new { elementId = "QF2", kind = "Earthed", reason = "" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await op.DeleteAsync($"/api/grid/tags/{tag.Id}")).StatusCode);
        Assert.Empty((await viewer.GetFromJsonAsync<List<TagDto>>("/api/grid/tags"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.DeleteAsync($"/api/grid/tags/{tag.Id}")).StatusCode);
    }

    [Fact]
    public async Task TagPlacementIsAuditedWithSourceGridTags()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var op = factory.Client(ApiTestFactory.OperatorKey);
        var placed = await op.PostAsJsonAsync("/api/grid/tags", new { elementId = "TR1", kind = "WorkInProgress", reason = "audit check" });
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var buffer = factory.Services.GetRequiredService<Application.Events.RecentEventBuffer>();
        Assert.Contains(buffer.GetRecent(500), e => e.Category == "Audit" && e.Source == "grid:tags"
            && e.Message.Contains("TR1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HistoryEndpointReturnsRecordedReportedPoints()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"));
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        var coordinator = factory.Services.GetRequiredService<GridCoordinator>();
        for (var i = 0; i < 3; i++) await coordinator.StepAsync(false, CancellationToken.None);

        var measurements = await viewer.GetFromJsonAsync<List<MeasurementDto>>("/api/grid/measurements");
        var id = measurements!.First().Id;
        var encoded = Uri.EscapeDataString(id);
        var points = await viewer.GetFromJsonAsync<List<HistoryPointDto>>($"/api/grid/history/{encoded}?points=10");
        Assert.NotNull(points);
        Assert.Equal(3, points.Count);
        Assert.All(points, p => Assert.True(p.Value is >= -1e9 and <= 1e9));
        Assert.True(points.Select(p => p.SimulationTime).SequenceEqual(points.Select(p => p.SimulationTime).OrderBy(t => t)));
        // Unknown measurement and non-positive points return empty lists, not errors.
        Assert.Empty((await viewer.GetFromJsonAsync<List<HistoryPointDto>>($"/api/grid/history/{Uri.EscapeDataString("no:such:id")}?points=10"))!);
        Assert.Empty((await viewer.GetFromJsonAsync<List<HistoryPointDto>>($"/api/grid/history/{encoded}?points=0"))!);
    }

    [Fact]
    public async Task CookieTagWritesRequireCsrf()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings(("Grid:Enabled", "false")));
        await using var browser = BrowserSession.Open(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SignInAsync("eva", BrowserSession.EvaPassword)).StatusCode);
        const string json = "{\"elementId\":\"QF1\",\"kind\":\"Information\",\"reason\":\"csrf\"}";
        static HttpRequestMessage Request(string? csrfToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/grid/tags")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (csrfToken is not null) request.Headers.Add("X-CSRF-TOKEN", csrfToken);
            return request;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Client.SendAsync(Request(null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.SendAsync(Request(await browser.ApiTokenAsync()))).StatusCode);
    }

    private sealed record GridCommandResultDto(bool Accepted, string Reason, string Message);
    private sealed record TagDto(string Id, string ElementId, string Kind, string Reason, string PlacedBy);
    private sealed record MeasurementDto(string Id);
    private sealed record HistoryPointDto(string SimulationTime, double Value, string Quality);
}
