using Application.Detection;
using Application.Events;
using global::Application.Grid;
using Application.Realtime;
using Security.Grid.Detection;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection;

public sealed class GridDetectionEngineTests
{
    [Fact]
    public Task RulesAndMetricsAreExposedLikeTheWaterEngine()
    {
        var rig = new SecurityGridTestRig();

        Assert.Equal(10, rig.Detection.Rules.Count);
        Assert.All(rig.Detection.Rules, r => Assert.StartsWith("grid-", r.Id));
        Assert.Equal(10, rig.Detection.Metrics.Count);
        Assert.All(rig.Detection.Metrics, m => Assert.True(m.Enabled));

        return Task.CompletedTask;
    }

    [Fact]
    public Task UpdateSettingsValidatesAndApplies()
    {
        var rig = new SecurityGridTestRig();
        var updated = rig.Detection.Rules.First(r => r.Id == "grid-mass-switching").Settings with
        {
            Threshold = 5,
            WindowSeconds = 30,
            CooldownSeconds = 10,
            Severity = DetectionSeverity.Low
        };

        Assert.True(rig.Detection.UpdateSettings(updated));
        Assert.Equal(5, rig.Detection.Rules.First(r => r.Id == "grid-mass-switching").Settings.Threshold);

        Assert.False(rig.Detection.UpdateSettings(updated with { RuleId = "no-such-rule" }));
        Assert.False(rig.Detection.UpdateSettings(updated with { WindowSeconds = 0 }));
        Assert.False(rig.Detection.UpdateSettings(updated with { Threshold = double.NaN }));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task FindingsBecomeDetectionsIncidentsAndRealtimeMessages()
    {
        var rig = new SecurityGridTestRig();
        await using var subscription = rig.Realtime.Subscribe([RealtimeTopics.Detections], 16);

        rig.View.SetSwitchState("SW-L01", false, global::Domain.Grid.Snapshots.GridChangeCause.Unattributed, "attacker");
        rig.View.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L01", "forced open", "attacker", global::Domain.Grid.Snapshots.GridChangeCause.Unattributed));
        await rig.StepAsync();

        var detections = await rig.DetectionsAsync();
        var detection = Assert.Single(detections, d => d.RuleId == "grid-unsolicited-switching");
        Assert.Equal("grid-unsolicited-switching", detection.RuleId);
        Assert.NotNull(detection.IncidentId);
        Assert.True(subscription.Reader.TryRead(out var message));
        var published = Assert.IsType<DetectionRecord>(message.Payload);
        Assert.Equal(detection.Id, published.Id);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Detection && e.Source == "grid-unsolicited-switching");
        var incident = await rig.IncidentStore.GetAsync(detection.IncidentId!.Value, CancellationToken.None);
        Assert.NotNull(incident);
    }

    [Fact]
    public async Task DeduplicationAndCooldownSuppressRepeatedFindings()
    {
        var rig = new SecurityGridTestRig();
        rig.View.SetSwitchState("SW-L01", false, global::Domain.Grid.Snapshots.GridChangeCause.Unattributed, "attacker");
        for (var i = 0; i < 10; i++)
        {
            rig.View.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L01", "forced open", "attacker", global::Domain.Grid.Snapshots.GridChangeCause.Unattributed));
            await rig.StepAsync();
        }

        var detections = await rig.DetectionsAsync();
        Assert.Single(detections, d => d.RuleId == "grid-unsolicited-switching"); // cooldown 60 s suppresses the rest
        var metrics = rig.Detection.Metrics.First(m => m.RuleId == "grid-unsolicited-switching");
        Assert.True(metrics.Suppressed >= 9);
        Assert.True(metrics.Findings >= 1);
    }

    [Fact]
    public async Task DetectionsAreCorrelatedToTheActiveScenarioRun()
    {
        var rig = new SecurityGridTestRig();
        var start = await rig.Scenarios.StartAsync("grid-industroyer-breaker-opening", SecurityGridTestRig.Instructor, CancellationToken.None);
        Assert.True(start.Succeeded);
        var runId = rig.Scenarios.ActiveRunId!.Value;

        for (var i = 0; i < 8; i++)
            await rig.StepAsync();

        var detections = await rig.DetectionsAsync();
        Assert.NotEmpty(detections);
        Assert.All(detections, d => Assert.Equal(runId, d.ScenarioRunId));
    }

    [Fact]
    public async Task ScopeChangeResetsRuleStateSoANewRunRedetects()
    {
        var rig = new SecurityGridTestRig();
        var first = await rig.RunScenarioAsync("grid-industroyer-breaker-opening");
        Assert.Equal(global::Application.Scenarios.ScenarioOutcome.Detected, first.Outcome);
        var second = await rig.RunScenarioAsync("grid-industroyer-breaker-opening");
        Assert.Equal(global::Application.Scenarios.ScenarioOutcome.Detected, second.Outcome);
        Assert.NotEqual(first.RunId, second.RunId);
        var detections = await rig.DetectionsAsync();
        Assert.Contains(detections, d => d.ScenarioRunId == first.RunId);
        Assert.Contains(detections, d => d.ScenarioRunId == second.RunId);
    }
}
