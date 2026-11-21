using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Grid;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Security.Grid.Detection;
using Security.Grid.Scenarios;
using Xunit;

namespace IntegrationTests.Api;

/// <summary>
/// Grid security REST surface: /api/grid/scenarios and /api/grid/detections with the same role conventions as the water
/// endpoints. The grid simulation itself is Agent A's; here a scripted fake view drives the observers directly.
/// </summary>
public sealed class GridSecurityApiTests
{
    private static (WebApplicationFactory<Program> Factory, SecurityFakeGridView View, SecurityFakeGridControl Control) Create()
    {
        var view = new SecurityFakeGridView();
        var training = new SecurityFakeGridTraining(view);
        var control = new SecurityFakeGridControl(view);
        var factory = ApiTestFactory.Create().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IGridView>(view);
            services.AddSingleton<IGridTraining>(training);
            services.AddSingleton<IGridControl>(control);
        }));
        return (factory, view, control);
    }

    private static async Task StepAsync(WebApplicationFactory<Program> factory, SecurityFakeGridView view, int count)
    {
        var scenarios = factory.Services.GetRequiredService<GridScenarioEngine>();
        var detection = factory.Services.GetRequiredService<GridDetectionEngine>();
        for (var i = 0; i < count; i++)
        {
            var report = view.Step(1);
            await scenarios.OnGridStepAsync(report, CancellationToken.None);
            await detection.OnGridStepAsync(report, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReadEndpointsRequireViewerAndReturnGridData()
    {
        var (factory, _, _) = Create();
        using var _ = factory;
        using var anonymous = factory.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/grid/scenarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/grid/detections/rules")).StatusCode);

        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        var catalog = await viewer.GetJsonAsync("/api/grid/scenarios");
        Assert.True(catalog.GetArrayLength() >= 8);
        Assert.All(catalog.EnumerateArray(), s => Assert.StartsWith("grid-", s.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.GetAsync("/api/grid/scenarios/current")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/grid/scenarios/runs")).StatusCode);
        var rules = await viewer.GetJsonAsync("/api/grid/detections/rules");
        Assert.True(rules.GetArrayLength() >= 10);
        Assert.All(rules.EnumerateArray(), r => Assert.StartsWith("grid-", r.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/grid/detections/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/grid/detections")).StatusCode);
    }

    [Fact]
    public async Task ViewerCannotControlScenariosAndTheDenialIsAudited()
    {
        var (factory, _, _) = Create();
        using var _ = factory;
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/grid/scenarios/grid-unauthorized-commands/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/grid/scenarios/active/stop", null)).StatusCode);

        var events = await admin.GetJsonAsync("/api/events?category=AuthorizationDenied&search=grid");
        Assert.True(events.GetProperty("totalCount").GetInt32() > 0);
    }

    [Fact]
    public async Task UnknownScenarioIs404BeforeRateLimiting()
    {
        var (factory, _, _) = Create();
        using var _ = factory;
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/grid/scenarios/no-such-scenario/start", null)).StatusCode);
    }

    [Fact]
    public async Task ScenarioRunsThroughTheApiAndIsDetected()
    {
        var (factory, view, control) = Create();
        using var _ = factory;
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var start = await admin.PostAsync("/api/grid/scenarios/grid-unauthorized-commands/start", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var runPath = start.Headers.Location!.OriginalString;
        Assert.StartsWith("/api/grid/scenarios/runs/", runPath);

        var scenarios = factory.Services.GetRequiredService<GridScenarioEngine>();
        for (var i = 0; i < 60 && scenarios.ActiveRunId is not null; i++)
            await StepAsync(factory, view, 1);

        var run = await admin.GetJsonAsync(runPath);
        Assert.Equal("Completed", run.GetProperty("state").GetString());
        Assert.Equal("Detected", run.GetProperty("outcome").GetString());
        var runId = run.GetProperty("runId").GetString();
        Assert.Contains(run.GetProperty("observedDetections").EnumerateArray(), d => d.GetString() == "grid-unauthorized-command");

        // Viewer commands were rejected by the platform; the replay attempt too.
        Assert.Contains(control.Calls, c => c.Contains("guest-viewer") && c.Contains("NotAuthorized"));
        Assert.Contains(control.Calls, c => c.Contains("instructor") && c.Contains("Replay"));

        var detections = await admin.GetJsonAsync($"/api/grid/detections?scenarioRunId={runId}");
        Assert.Contains(detections.EnumerateArray(), d => d.GetProperty("ruleId").GetString() == "grid-unauthorized-command");
        var incidents = await admin.GetJsonAsync($"/api/incidents?scenarioRunId={runId}");
        Assert.True(incidents.GetProperty("totalCount").GetInt32() > 0);
        var runs = await admin.GetJsonAsync("/api/grid/scenarios/runs");
        Assert.Contains(runs.EnumerateArray(), r => r.GetProperty("runId").GetString() == runId);
    }

    [Fact]
    public async Task ScenarioLifecycleEndpointsWork()
    {
        var (factory, view, _) = Create();
        using var _ = factory;
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        await admin.PostAsync("/api/grid/scenarios/grid-rtu-denial-of-service/start", null);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/grid/scenarios/active/pause", null)).StatusCode);
        Assert.Equal("Paused", (await admin.GetJsonAsync("/api/grid/scenarios/current")).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/grid/scenarios/active/resume", null)).StatusCode);

        await StepAsync(factory, view, 5);
        var stopped = await (await admin.PostAsync("/api/grid/scenarios/active/stop", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Aborted", stopped!.GetProperty("run").GetProperty("outcome").GetString());

        var replay = await admin.PostAsync($"/api/grid/scenarios/runs/{stopped.GetProperty("run").GetProperty("runId").GetString()}/replay", null);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        var current = await admin.GetJsonAsync("/api/grid/scenarios/current");
        Assert.Equal(stopped.GetProperty("run").GetProperty("runId").GetString(), current.GetProperty("replayOf").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/grid/scenarios/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.GetAsync("/api/grid/scenarios/current")).StatusCode);
    }

    /// <summary>
    /// End-to-end on the REAL grid engine: the API host runs the actual <see cref="GridEngine"/>/
    /// <see cref="GridCoordinator"/> with the real detection and scenario engines (only the hosted wall-clock loop is
    /// disabled); the test drives <see cref="GridCoordinator.StepAsync"/> directly, so it is deterministic. A full
    /// simulated day on the default network and default seed must produce zero grid findings.
    /// </summary>
    [Fact]
    public async Task NormalDayOnTheRealGridEngineProducesZeroFindings()
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"), ("Grid:StepSeconds", "60"));
        var coordinator = factory.Services.GetRequiredService<GridCoordinator>();
        var detection = factory.Services.GetRequiredService<GridDetectionEngine>();

        // One simulated day at 60 s steps = 1440 steps from the default 08:00 start.
        for (var i = 0; i < 1440; i++)
            await coordinator.StepAsync(false, CancellationToken.None);

        using var admin = factory.Client(ApiTestFactory.AdminKey);
        var detections = await admin.GetJsonAsync("/api/grid/detections?limit=1000");
        Assert.Equal(0, detections.GetArrayLength());
        Assert.All(detection.Metrics, m => Assert.Equal(1440, m.Evaluations));
        var incidents = await admin.GetJsonAsync("/api/incidents?activeOnly=false");
        Assert.Equal(0, incidents.GetProperty("totalCount").GetInt32());
    }

    public static TheoryData<string> RealScenarioIds =>
        [.. Security.Grid.Scenarios.Catalog.DefaultGridScenarios.Create().Select(s => s.Id)];

    /// <summary>Each catalog grid scenario, started through the REST API on the real engine with the real training and
    /// control services, ends Detected with its expected rule ids observed, and the network is sane again afterwards.</summary>
    [Theory]
    [MemberData(nameof(RealScenarioIds))]
    public async Task CatalogScenarioOnTheRealGridEngineIsDetectedAndRestoresTheNetwork(string scenarioId)
    {
        using var factory = ApiTestFactory.Create(("Grid:Enabled", "false"), ("Grid:StepSeconds", "1"));
        var coordinator = factory.Services.GetRequiredService<GridCoordinator>();
        var scenarios = factory.Services.GetRequiredService<GridScenarioEngine>();
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        // Prime the engine a few steps so targets resolve against a converged network.
        for (var i = 0; i < 5; i++)
            await coordinator.StepAsync(false, CancellationToken.None);

        var start = await admin.PostAsync($"/api/grid/scenarios/{scenarioId}/start", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var runPath = start.Headers.Location!.OriginalString;

        for (var i = 0; i < 400 && scenarios.ActiveRunId is not null; i++)
            await coordinator.StepAsync(false, CancellationToken.None);

        var run = await admin.GetJsonAsync(runPath);
        Assert.Equal("Completed", run.GetProperty("state").GetString());
        Assert.Equal("Detected", run.GetProperty("outcome").GetString());
        var expected = run.GetProperty("expectedDetections").EnumerateArray().Select(d => d.GetString()).ToArray();
        var observed = run.GetProperty("observedDetections").EnumerateArray().Select(d => d.GetString()).ToArray();
        Assert.All(expected, e => Assert.Contains(e, observed));

        // Let the network settle after the run's cleanup/restore, then it must be back to a sane state (admin = truth).
        for (var i = 0; i < 70; i++)
            await coordinator.StepAsync(false, CancellationToken.None);
        var grid = await admin.GetJsonAsync("/api/grid");
        Assert.True(grid.GetProperty("converged").GetBoolean());
        Assert.All(grid.GetProperty("switches").EnumerateArray(), s =>
            Assert.True(s.GetProperty("closed").GetBoolean() == (s.GetProperty("id").GetString() != "TIE"), s.GetProperty("id").GetString()));
        Assert.All(grid.GetProperty("branches").EnumerateArray(), b =>
            Assert.Equal("None", b.GetProperty("fault").GetString()));
        Assert.DoesNotContain(grid.GetProperty("measurements").EnumerateArray(), m => m.GetProperty("isSpoofed").GetBoolean());
        Assert.DoesNotContain(grid.GetProperty("alarms").EnumerateArray(), a => a.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task RuleSettingsCanBeTunedByAdministratorOnly()
    {
        var (factory, _, _) = Create();
        using var _ = factory;
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var body = new { enabled = true, severity = "Low", threshold = 5, windowSeconds = 30, cooldownSeconds = 10 };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/grid/detections/rules/grid-mass-switching", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/grid/detections/rules/no-such-rule", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/grid/detections/rules/grid-mass-switching",
            new { enabled = true, severity = "Low", threshold = -1, windowSeconds = 30, cooldownSeconds = 10 })).StatusCode);

        var ok = await admin.PutAsJsonAsync("/api/grid/detections/rules/grid-mass-switching", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var rules = await admin.GetJsonAsync("/api/grid/detections/rules");
        var mass = rules.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "grid-mass-switching");
        Assert.Equal(5, mass.GetProperty("settings").GetProperty("threshold").GetDouble());
        Assert.Equal(30, mass.GetProperty("settings").GetProperty("windowSeconds").GetInt32());
    }
}
