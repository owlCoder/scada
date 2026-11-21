using Application.Events;
using Application.Identity;
using global::Application.Scenarios;
using Security.Grid.Scenarios;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Scenarios;

public sealed class GridScenarioEngineTests
{
    [Fact]
    public Task CatalogIsExposedAndValid()
    {
        var rig = new SecurityGridTestRig();
        Assert.Equal(8, rig.Scenarios.Scenarios.Count);
        Assert.All(rig.Scenarios.Scenarios, s => Assert.StartsWith("grid-", s.Id));
        Assert.All(rig.Scenarios.Scenarios, s => Assert.NotEmpty(s.ExpectedDetections));
        Assert.All(rig.Scenarios.Scenarios, s => Assert.NotEmpty(s.Steps));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ViewerCannotControlScenariosAndTheDenialIsAudited()
    {
        var rig = new SecurityGridTestRig();

        var start = await rig.Scenarios.StartAsync("grid-industroyer-breaker-opening", SecurityGridTestRig.Viewer, CancellationToken.None);
        Assert.False(start.Succeeded);
        Assert.Null(rig.Scenarios.ActiveRunId);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.AuthorizationDenied && e.Actor == "guest" && e.Source == "grid-scenario-engine");

        Assert.False((await rig.Scenarios.PauseAsync(SecurityGridTestRig.Viewer, CancellationToken.None)).Succeeded);
        Assert.False((await rig.Scenarios.StopAsync(SecurityGridTestRig.Viewer, CancellationToken.None)).Succeeded);
        Assert.False((await rig.Scenarios.ResetAsync(SecurityGridTestRig.Viewer, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task OnlyOneRunCanBeActiveAndUnknownScenariosAreRejected()
    {
        var rig = new SecurityGridTestRig();

        Assert.False((await rig.Scenarios.StartAsync("does-not-exist", SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        Assert.True((await rig.Scenarios.StartAsync("grid-rtu-denial-of-service", SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        var second = await rig.Scenarios.StartAsync("grid-unauthorized-commands", SecurityGridTestRig.Instructor, CancellationToken.None);
        Assert.False(second.Succeeded);
        Assert.Equal("grid-rtu-denial-of-service", second.Run!.ScenarioId);
    }

    [Fact]
    public async Task PauseResumeStopResetAndReplayWorkLikeTheWaterEngine()
    {
        var rig = new SecurityGridTestRig();
        Assert.True((await rig.Scenarios.StartAsync("grid-rtu-denial-of-service", SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        await rig.RunForAsync(5);

        Assert.True((await rig.Scenarios.PauseAsync(SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        var paused = rig.Scenarios.Current!.Elapsed;
        await rig.RunForAsync(10);
        Assert.Equal(paused, rig.Scenarios.Current!.Elapsed);
        Assert.False((await rig.Scenarios.PauseAsync(SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);

        Assert.True((await rig.Scenarios.ResumeAsync(SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        await rig.RunForAsync(5);
        Assert.True(rig.Scenarios.Current!.Elapsed > paused);

        var stopped = await rig.Scenarios.StopAsync(SecurityGridTestRig.Instructor, CancellationToken.None);
        Assert.Equal(ScenarioOutcome.Aborted, stopped.Run!.Outcome);
        Assert.Equal(ScenarioRunState.Stopped, stopped.Run.State);
        Assert.Null(rig.Scenarios.ActiveRunId);
        Assert.False((await rig.Scenarios.StopAsync(SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);

        var replay = await rig.Scenarios.ReplayAsync(stopped.Run.RunId, SecurityGridTestRig.Instructor, CancellationToken.None);
        Assert.True(replay.Succeeded);
        Assert.Equal(stopped.Run.RunId, replay.Run!.ReplayOf);
        Assert.False((await rig.Scenarios.ReplayAsync(Guid.NewGuid(), SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);

        Assert.True((await rig.Scenarios.ResetAsync(SecurityGridTestRig.Instructor, CancellationToken.None)).Succeeded);
        Assert.Null(rig.Scenarios.Current);
    }

    [Fact]
    public async Task StartingFailsClearlyWhenATargetCannotBeFound()
    {
        // The alternative network has no OLTC transformer.
        var rig = new SecurityGridTestRig(SecurityFakeGridNetworks.Alternative());

        var result = await rig.Scenarios.StartAsync("grid-oltc-manipulation", SecurityGridTestRig.Instructor, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot run on this network", result.Message);
        Assert.Contains("OLTC", result.Message);
        Assert.Null(rig.Scenarios.ActiveRunId);
    }

    [Fact]
    public async Task CompletedRunIsPersistedAndOutcomeIsEvaluated()
    {
        var rig = new SecurityGridTestRig();
        var run = await rig.RunScenarioAsync("grid-unauthorized-commands");

        Assert.Equal(ScenarioRunState.Completed, run.State);
        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        Assert.Contains("grid-unauthorized-command", run.ObservedDetections);
        Assert.All(run.Actions, a => Assert.True(a.Succeeded, $"{a.Action}: {a.Detail}"));
        var stored = await rig.RunStore.GetAsync(run.RunId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ScenarioOutcome.Detected, stored!.Outcome);
        var listed = await rig.Scenarios.ListRunsAsync(50, CancellationToken.None);
        Assert.Contains(listed, r => r.RunId == run.RunId);
    }

    [Fact]
    public async Task MissedOutcomeWhenTheExpectedRuleIsDisabled()
    {
        var rig = new SecurityGridTestRig();
        var disabled = rig.Detection.Rules.First(r => r.Id == "grid-unauthorized-command").Settings with { Enabled = false };
        Assert.True(rig.Detection.UpdateSettings(disabled));

        var run = await rig.RunScenarioAsync("grid-unauthorized-commands");

        Assert.Equal(ScenarioOutcome.Missed, run.Outcome);
        Assert.DoesNotContain("grid-unauthorized-command", run.ObservedDetections);
    }

    [Fact]
    public async Task ScenarioActionsOnlyUseTrainingControlAndView()
    {
        var rig = new SecurityGridTestRig();
        var run = await rig.RunScenarioAsync("grid-rtu-denial-of-service");

        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        Assert.Contains("grid-rtu-communication-loss", run.ObservedDetections);
        Assert.Contains("grid-unsolicited-switching", run.ObservedDetections);
        Assert.Contains(rig.Training.Calls, c => c.StartsWith("SetRtuOnline RTU-1 = False", StringComparison.Ordinal));
        Assert.Contains(rig.Training.Calls, c => c.StartsWith("ForceSwitch SW-L01 closed=False", StringComparison.Ordinal));
        // Cleanup restored the RTU and closed the breaker again, then training defaults were restored.
        Assert.Contains(rig.Training.Calls, c => c.StartsWith("SetRtuOnline RTU-1 = True", StringComparison.Ordinal));
        Assert.Contains(rig.Training.Calls, c => c.StartsWith("ForceSwitch SW-L01 closed=True", StringComparison.Ordinal));
        Assert.Contains(rig.Training.Calls, c => c.StartsWith("RestoreTrainingDefaults", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndustroyerScenarioIssuesForcedOpensAndIsDetected()
    {
        var rig = new SecurityGridTestRig();
        var run = await rig.RunScenarioAsync("grid-industroyer-breaker-opening");

        Assert.Equal(ScenarioOutcome.Detected, run.Outcome);
        Assert.Contains("grid-unsolicited-switching", run.ObservedDetections);
        Assert.Contains("grid-mass-switching", run.ObservedDetections);
        var forced = rig.Training.Calls.Where(c => c.StartsWith("ForceSwitch", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, forced.Length); // 3 attack opens + 3 cleanup closes
    }

    [Fact]
    public async Task TargetsResolveOnTwoDifferentNetworks()
    {
        var defaultRig = new SecurityGridTestRig(SecurityFakeGridNetworks.Default());
        var altRig = new SecurityGridTestRig(SecurityFakeGridNetworks.Alternative());

        var onDefault = await defaultRig.RunScenarioAsync("grid-industroyer-breaker-opening");
        Assert.Equal(ScenarioOutcome.Detected, onDefault.Outcome);

        // The same scenario resolves its targets on the alternative network too and is detected there.
        var onAlt = await altRig.RunScenarioAsync("grid-industroyer-breaker-opening");
        Assert.Equal(ScenarioOutcome.Detected, onAlt.Outcome);

        // The load-shedding scenario needs 8 loads; the alternative network has 3, so starting it fails clearly.
        var tooFew = await altRig.Scenarios.StartAsync("grid-load-shedding-spoofing", SecurityGridTestRig.Instructor, CancellationToken.None);
        Assert.False(tooFew.Succeeded);
        Assert.Contains("loads", tooFew.Message);
        Assert.Null(altRig.Scenarios.ActiveRunId);
    }
}
