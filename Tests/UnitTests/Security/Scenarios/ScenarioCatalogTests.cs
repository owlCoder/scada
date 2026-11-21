using Application.Detection;
using Application.Scenarios;
using Security.Scenarios.Catalog;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Scenarios;

public sealed class ScenarioCatalogTests
{
    private static readonly string[] WindowedRules =
    [
        "repeated-rejected-commands", "unauthorized-access", "command-replay", "excessive-control-requests",
        "invalid-register-operations", "network-segmentation-violation", "conflicting-operator-commands", "operator-misuse",
        "mass-balance-inconsistency", "communication-failure", "historian-integrity"
    ];

    public static TheoryData<string> ScenarioIds => [.. DefaultScenarios.Create().Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public async Task EveryScenarioIsDetectedByAllExpectedRules(string scenarioId)
    {
        var rig = new TestRig();
        await rig.RunForAsync(10);

        var run = await rig.RunScenarioAsync(scenarioId);

        Assert.Equal(ScenarioRunState.Completed, run.State);
        Assert.All(run.Actions, action => Assert.True(action.Succeeded, $"{action.Action}: {action.Detail}"));
        var missing = run.ExpectedDetections.Except(run.ObservedDetections).ToArray();
        Assert.True(missing.Length == 0, $"Missing: {string.Join(", ", missing)}; observed: {string.Join(", ", run.ObservedDetections)}; " +
            string.Join(", ", run.Measurements.Select(m => $"{m.Key}={m.Value}")));
        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        var stored = await rig.RunStore.GetAsync(run.RunId, CancellationToken.None);
        Assert.Equal(ScenarioOutcome.Detected, stored!.Outcome);
    }

    [Fact]
    public async Task ScenariosRunBackToBackInOneProcessAreEachDetected()
    {
        var rig = new TestRig();
        await rig.RunForAsync(300);
        var results = new List<string>();
        string[] previousExpected = [];

        foreach (var scenario in DefaultScenarios.Create())
        {
            var run = await rig.RunScenarioAsync(scenario.Id);
            if (run.Outcome != ScenarioOutcome.Detected)
                results.Add($"{scenario.Id}: {run.Outcome}, missing {string.Join(", ", run.ExpectedDetections.Except(run.ObservedDetections))}");
            // Event- and window-based rules the previous scenario triggered must not leak into this run. Condition rules
            // (for example a tank that is genuinely still overfull) may legitimately continue.
            var carriedOver = previousExpected.Except(scenario.ExpectedDetections).Intersect(run.ObservedDetections)
                .Intersect(WindowedRules).ToArray();
            if (carriedOver.Length > 0)
                results.Add($"{scenario.Id}: detections carried over from the previous run: {string.Join(", ", carriedOver)}");
            previousExpected = [.. scenario.ExpectedDetections];
            Assert.All(run.Actions, a => Assert.True(a.Succeeded && !a.Detail.Contains("CommunicationUnavailable"),
                $"{scenario.Id}: {a.Action}: {a.Detail}"));
            // Let genuine after-effects (for example an overfull tank draining) settle, as an instructor would.
            await rig.RunForAsync(120);
        }

        Assert.True(results.Count == 0, string.Join(Environment.NewLine, results));
    }

    [Fact]
    public async Task NormalOperationProducesNoDetections()
    {
        var rig = new TestRig();

        await rig.RunForAsync(600);

        var detections = await rig.DetectionStore.QueryAsync(new DetectionQuery(Limit: 100), CancellationToken.None);
        Assert.True(detections.Count == 0,
            "Unexpected detections: " + string.Join("; ", detections.Select(d => $"{d.RuleId}: {d.Title}")));
    }

    [Fact]
    public Task CatalogCoversEveryDetectionRule()
    {
        var rig = new TestRig();
        var expected = DefaultScenarios.Create().SelectMany(s => s.ExpectedDetections).ToHashSet(StringComparer.Ordinal);

        Assert.All(rig.Detection.Rules, rule => Assert.Contains(rule.Id, expected));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ScenarioRestoresTrainingDefaultsWhenItEnds()
    {
        var rig = new TestRig();

        await rig.RunScenarioAsync("sensor-spoofing");
        var plant = rig.Training.Snapshot();
        Assert.DoesNotContain(plant.Sensors, s => s.IsSpoofed);

        await rig.RunScenarioAsync("process-safety-violation");
        Assert.False(rig.Training.Snapshot().InterlocksBypassed);

        await rig.RunScenarioAsync("command-frequency");
        Assert.Equal(global::Domain.Process.Devices.ControlMode.Automatic, rig.Training.Snapshot().Valve("V-301")!.Mode);
    }
}
