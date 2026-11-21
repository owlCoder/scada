using global::Application.Scenarios;
using Security.Grid.Scenarios.Catalog;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Scenarios;

public sealed class GridScenarioCatalogTests
{
    public static TheoryData<string> ScenarioIds => [.. DefaultGridScenarios.Create().Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public Task EveryDefinitionIsValid(string scenarioId)
    {
        var definition = DefaultGridScenarios.Create().Single(s => s.Id == scenarioId);
        Assert.Empty(definition.Validate());
        Assert.All(definition.Actions, a => Assert.True(a.AtSeconds < definition.DurationSeconds));

        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public async Task EveryScenarioRunsToCompletionAndIsDetected(string scenarioId)
    {
        var rig = new SecurityGridTestRig();
        await rig.RunForAsync(5);

        var run = await rig.RunScenarioAsync(scenarioId, maxSeconds: 400);

        Assert.Equal(ScenarioRunState.Completed, run.State);
        Assert.All(run.Actions, action => Assert.True(action.Succeeded, $"{action.Action}: {action.Detail}"));
        var missing = run.ExpectedDetections.Except(run.ObservedDetections).ToArray();
        Assert.True(missing.Length == 0,
            $"Missing: {string.Join(", ", missing)}; observed: {string.Join(", ", run.ObservedDetections)}");
        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
    }

    [Fact]
    public async Task ScenariosRunBackToBackInOneProcess()
    {
        var rig = new SecurityGridTestRig();
        var failures = new List<string>();
        foreach (var scenario in DefaultGridScenarios.Create())
        {
            var run = await rig.RunScenarioAsync(scenario.Id, maxSeconds: 400);
            if (run.Outcome != ScenarioOutcome.Detected)
                failures.Add($"{scenario.Id}: {run.Outcome}, missing {string.Join(", ", scenario.ExpectedDetections.Except(run.ObservedDetections))}");
            // Let after-effects settle between runs, as an instructor would.
            await rig.RunForAsync(70);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task ScenariosResolveTargetsOnTheAlternativeNetwork()
    {
        // Scenarios whose generic targets exist on the alternative 20 kV network run there too.
        var rig = new SecurityGridTestRig(SecurityFakeGridNetworks.Alternative());
        foreach (var scenarioId in new[] { "grid-protection-tampering-fault", "grid-false-data-injection", "grid-rtu-denial-of-service", "grid-unauthorized-commands" })
        {
            var run = await rig.RunScenarioAsync(scenarioId, maxSeconds: 400);
            Assert.True(run.Actions.All(a => a.Succeeded), $"{scenarioId}: {string.Join("; ", run.Actions.Where(a => !a.Succeeded).Select(a => a.Detail))}");
            Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        }
    }
}
