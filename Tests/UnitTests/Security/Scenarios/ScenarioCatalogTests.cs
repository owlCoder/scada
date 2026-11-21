using Application.Detection;
using Application.Scenarios;
using Security.Scenarios.Catalog;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Scenarios;

public sealed class ScenarioCatalogTests
{
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
