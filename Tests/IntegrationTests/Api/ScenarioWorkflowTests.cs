using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

/// <summary>Runs a scenario through the real-time loop (accelerated) and works the resulting incident through the API.</summary>
public sealed class ScenarioWorkflowTests
{
    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory() =>
        ApiTestFactory.Create(("Simulation:TimeScale", "50"));

    [Fact]
    public async Task ScenarioCompletesAsDetectedAndRaisesIncidents()
    {
        using var factory = Factory();
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var start = await admin.PostAsync("/api/scenarios/unauthorized-command/start", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var runPath = start.Headers.Location!.OriginalString;

        var run = await ApiTestFactory.EventuallyAsync(() => admin.GetJsonAsync(runPath),
            r => r.GetProperty("state").GetString() == "Completed", TimeSpan.FromSeconds(30));
        Assert.Equal("Completed", run.GetProperty("state").GetString());
        Assert.Equal("Detected", run.GetProperty("outcome").GetString());
        var runId = run.GetProperty("runId").GetString();

        var incidents = await admin.GetJsonAsync($"/api/incidents?scenarioRunId={runId}");
        Assert.True(incidents.GetProperty("totalCount").GetInt32() > 0);
        var detections = await admin.GetJsonAsync($"/api/detections?scenarioRunId={runId}");
        Assert.Contains(detections.EnumerateArray(), d => d.GetProperty("ruleId").GetString() == "unauthorized-access");
        var runs = await admin.GetJsonAsync("/api/scenarios/runs");
        Assert.Contains(runs.EnumerateArray(), r => r.GetProperty("runId").GetString() == runId);
    }

    [Fact]
    public async Task IncidentCanBeWorkedThroughItsLifecycleWithVersionChecks()
    {
        using var factory = Factory();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var ana = factory.Client(ApiTestFactory.AnaKey);
        await admin.PostAsync("/api/scenarios/data-integrity-violation/start", null);
        var page = await ApiTestFactory.EventuallyAsync(() => ana.GetJsonAsync("/api/incidents?activeOnly=true"),
            p => p.GetProperty("totalCount").GetInt32() > 0, TimeSpan.FromSeconds(30));
        var incident = page.GetProperty("items")[0];
        var id = incident.GetProperty("id").GetString();
        var version = incident.GetProperty("version").GetInt32();

        var ack = await ana.PostAsJsonAsync($"/api/incidents/{id}/status", new { status = "Acknowledged", expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var stale = await ana.PostAsJsonAsync($"/api/incidents/{id}/status", new { status = "InProgress", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var noNotes = await ana.PostAsJsonAsync($"/api/incidents/{id}/status", new { status = "Resolved" });
        Assert.NotEqual(HttpStatusCode.OK, noNotes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/api/incidents/{id}/comments", new { text = "Checked historian" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/api/incidents/{id}/assign", new { assignee = "ana" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/api/incidents/{id}/status",
            new { status = "Resolved", notes = "Restored from backup" })).StatusCode);

        var details = await ana.GetJsonAsync($"/api/incidents/{id}");
        Assert.Equal("Resolved", details.GetProperty("incident").GetProperty("status").GetString());
        Assert.Equal("ana", details.GetProperty("incident").GetProperty("assignedTo").GetString());
        Assert.Single(details.GetProperty("comments").EnumerateArray());
        var timeline = await ana.GetJsonAsync($"/api/incidents/{id}/timeline");
        Assert.True(timeline.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task ScenarioCanBePausedResumedStoppedAndReplayed()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        await admin.PostAsync("/api/scenarios/sensor-spoofing/start", null);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/scenarios/active/pause", null)).StatusCode);
        Assert.Equal("Paused", (await admin.GetJsonAsync("/api/scenarios/current")).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/scenarios/active/resume", null)).StatusCode);
        var stopped = await (await admin.PostAsync("/api/scenarios/active/stop", null)).Content.ReadFromJsonAsync<JsonElement>();
        var run = stopped.GetProperty("run");
        Assert.Equal("Aborted", run.GetProperty("outcome").GetString());

        var replay = await admin.PostAsync($"/api/scenarios/runs/{run.GetProperty("runId").GetString()}/replay", null);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        var current = await admin.GetJsonAsync("/api/scenarios/current");
        Assert.Equal(run.GetProperty("runId").GetString(), current.GetProperty("replayOf").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/scenarios/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.GetAsync("/api/scenarios/current")).StatusCode);
    }
}
