using Application.Detection;
using Application.Scenarios;
using Domain.Grid.Snapshots;
using Security.Grid.Scenarios.Catalog;
using UnitTests.Grid.Support;
using Xunit;

namespace UnitTests.Grid;

/// <summary>
/// End-to-end tests of the merged grid stack on the REAL engine: <see cref="GridRealEngineRig"/> drives
/// <see cref="Application.Grid.GridCoordinator.StepAsync"/> (engine step + observer fan-out) with the real
/// <see cref="Security.Grid.Detection.GridDetectionEngine"/>, <see cref="Security.Grid.Scenarios.GridScenarioEngine"/>,
/// control and training services over the default network and default seed — deterministic, no wall-clock waits.
/// </summary>
public sealed class GridRealEngineSecurityTests
{
    [Fact]
    public async Task NormalSimulatedDayOnTheRealEngineProducesZeroFindings()
    {
        var rig = new GridRealEngineRig(stepSeconds: 60);

        // One simulated day at 60 s steps = 1440 steps, starting at the default 08:00.
        for (var i = 0; i < 1440; i++)
            await rig.StepAsync();

        var detections = await rig.DetectionsAsync();
        Assert.True(detections.Count == 0,
            "Unexpected detections: " + string.Join("; ", detections.Select(d => $"{d.RuleId}: {d.Title} :: {d.Description}")));
        Assert.All(rig.Detection.Metrics, m => Assert.Equal(1440, m.Evaluations));
        Assert.Empty((await rig.Incidents.QueryAsync(new(), CancellationToken.None)).Items);
    }

    public static TheoryData<string> ScenarioIds => [.. DefaultGridScenarios.Create().Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public async Task EveryCatalogScenarioOnTheRealEngineIsDetectedAndRestoresTheNetwork(string scenarioId)
    {
        var rig = new GridRealEngineRig(stepSeconds: 1);
        await rig.RunForAsync(5);

        var run = await rig.RunScenarioAsync(scenarioId, maxSeconds: 400);

        Assert.Equal(ScenarioRunState.Completed, run.State);
        Assert.All(run.Actions, action => Assert.True(action.Succeeded, $"{action.Action}: {action.Detail}"));
        var missing = run.ExpectedDetections.Except(run.ObservedDetections, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            $"Missing: {string.Join(", ", missing)}; observed: {string.Join(", ", run.ObservedDetections)}");
        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        Assert.All(run.ExpectedDetections, expected => Assert.Contains(run.ObservedDetections, d => d == expected));

        // Let the network settle after the run's cleanup/restore, then it must be back to a sane state.
        await rig.RunForAsync(70);
        var snapshot = rig.Engine.Snapshot();
        var definition = rig.Engine.Definition;
        Assert.True(snapshot.Converged);
        Assert.All(snapshot.Switches, s => Assert.True(s.Closed == (s.Id != "TIE"), $"{s.Id} should be {(s.Id == "TIE" ? "open" : "closed")}"));
        Assert.All(snapshot.Branches, b => Assert.Equal(GridEquipmentFault.None, b.Fault));
        Assert.All(snapshot.Measurements, m => Assert.False(m.IsSpoofed));
        Assert.DoesNotContain(snapshot.Alarms, a => a.Active);
        Assert.All(snapshot.Buses, b => Assert.InRange(b.VoltagePu, definition.Buses.First(x => x.Id == b.Id).MinVoltagePu - 0.001,
            definition.Buses.First(x => x.Id == b.Id).MaxVoltagePu + 0.001));
        Assert.All(definition.Transformers, t => Assert.Equal(t.InitialTap, snapshot.Branch(t.Id)!.TapPosition));
        Assert.All(definition.Capacitors, c => Assert.Equal(c.InitialSteps, snapshot.Capacitors.First(x => x.Id == c.Id).StepsInService));
        Assert.All(definition.Protections, p =>
        {
            var s = snapshot.Protections.First(x => x.Id == p.Id);
            Assert.True(s.Enabled);
            Assert.Equal(p.PickupValue, s.PickupValue);
            Assert.False(s.Tripped);
        });
    }

    [Fact]
    public async Task AllScenariosRunBackToBackOnOneRealEngineInstance()
    {
        var rig = new GridRealEngineRig(stepSeconds: 1);
        var failures = new List<string>();
        foreach (var scenario in DefaultGridScenarios.Create())
        {
            var run = await rig.RunScenarioAsync(scenario.Id, maxSeconds: 400);
            if (run.Outcome != ScenarioOutcome.Detected || run.State != ScenarioRunState.Completed)
                failures.Add($"{scenario.Id}: {run.State}/{run.Outcome}, missing " +
                    $"{string.Join(", ", scenario.ExpectedDetections.Except(run.ObservedDetections, StringComparer.Ordinal))}");
            // Settle between runs, as an instructor would.
            await rig.RunForAsync(70);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        var snapshot = rig.Engine.Snapshot();
        Assert.DoesNotContain(snapshot.Alarms, a => a.Active);
        Assert.All(snapshot.Switches, s => Assert.True(s.Closed == (s.Id != "TIE"), $"{s.Id} switch state"));
    }

    [Fact]
    public async Task RealEngineScenarioDetectionsCorrelateToTheRunAndRaiseIncidents()
    {
        var rig = new GridRealEngineRig(stepSeconds: 1);
        await rig.RunForAsync(5);
        var run = await rig.RunScenarioAsync("grid-industroyer-breaker-opening", maxSeconds: 400);

        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        var detections = await rig.DetectionsAsync();
        Assert.All(detections.Where(d => d.ScenarioRunId == run.RunId), d => Assert.StartsWith("grid-", d.RuleId));
        var incidents = (await rig.Incidents.QueryAsync(new(), CancellationToken.None)).Items;
        Assert.Contains(incidents, i => i.ScenarioRunId == run.RunId);
        // Detections of the run are queryable by run id, like through the REST API.
        var scoped = await rig.DetectionStore.QueryAsync(new DetectionQuery(ScenarioRunId: run.RunId, Limit: 1000), CancellationToken.None);
        Assert.NotEmpty(scoped);
        Assert.All(scoped, d => Assert.Equal(run.RunId, d.ScenarioRunId));
    }
}
