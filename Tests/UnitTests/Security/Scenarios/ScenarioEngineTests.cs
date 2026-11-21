using Application.Events;
using Application.Identity;
using Application.Scenarios;
using Security.Scenarios;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Scenarios;

public sealed class ScenarioEngineTests
{
    private static readonly Actor Instructor = new("instructor", AccessRole.Administrator);

    [Fact]
    public async Task OnlyOneRunCanBeActiveAndUnknownScenariosAreRejected()
    {
        var rig = new TestRig();

        Assert.False((await rig.Scenarios.StartAsync("does-not-exist", Instructor, CancellationToken.None)).Succeeded);
        Assert.True((await rig.Scenarios.StartAsync("sensor-spoofing", Instructor, CancellationToken.None)).Succeeded);
        var second = await rig.Scenarios.StartAsync("command-replay", Instructor, CancellationToken.None);

        Assert.False(second.Succeeded);
        Assert.Equal("sensor-spoofing", second.Run!.ScenarioId);
    }

    [Fact]
    public async Task ViewerCannotControlScenarios()
    {
        var rig = new TestRig();
        var viewer = new Actor("guest", AccessRole.Viewer);

        Assert.False((await rig.Scenarios.StartAsync("sensor-spoofing", viewer, CancellationToken.None)).Succeeded);
        Assert.Null(rig.Scenarios.ActiveRunId);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.AuthorizationDenied && e.Actor == "guest");
    }

    [Fact]
    public async Task ElapsedTimeAdvancesOnlyWhileRunning()
    {
        var rig = new TestRig();
        await rig.Scenarios.StartAsync("sensor-spoofing", Instructor, CancellationToken.None);
        await rig.RunForAsync(10);

        Assert.True((await rig.Scenarios.PauseAsync(Instructor, CancellationToken.None)).Succeeded);
        var paused = rig.Scenarios.Current!.Elapsed;
        await rig.RunForAsync(20);
        Assert.Equal(paused, rig.Scenarios.Current!.Elapsed);
        Assert.Equal(ScenarioRunState.Paused, rig.Scenarios.Current.State);
        Assert.False((await rig.Scenarios.PauseAsync(Instructor, CancellationToken.None)).Succeeded);

        Assert.True((await rig.Scenarios.ResumeAsync(Instructor, CancellationToken.None)).Succeeded);
        await rig.RunForAsync(10);
        Assert.Equal(TimeSpan.FromSeconds(20), rig.Scenarios.Current!.Elapsed);
    }

    [Fact]
    public async Task StoppedRunIsAbortedAndTheEnvironmentIsRestored()
    {
        var rig = new TestRig();
        await rig.Scenarios.StartAsync("sensor-spoofing", Instructor, CancellationToken.None);
        await rig.RunForAsync(5);
        Assert.True(rig.Training.Snapshot().Sensor("LT-102")!.IsSpoofed);

        var stopped = await rig.Scenarios.StopAsync(Instructor, CancellationToken.None);

        Assert.Equal(ScenarioOutcome.Aborted, stopped.Run!.Outcome);
        Assert.Equal(ScenarioRunState.Stopped, stopped.Run.State);
        Assert.NotNull(stopped.Run.EndedAt);
        Assert.Null(rig.Scenarios.ActiveRunId);
        Assert.False(rig.Training.Snapshot().Sensor("LT-102")!.IsSpoofed);
        Assert.False((await rig.Scenarios.StopAsync(Instructor, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ReplayStartsTheSameScenarioLinkedToTheOriginalRun()
    {
        var rig = new TestRig();
        var original = await rig.RunScenarioAsync("data-integrity-violation");

        var replay = await rig.Scenarios.ReplayAsync(original.RunId, Instructor, CancellationToken.None);

        Assert.True(replay.Succeeded);
        Assert.Equal(original.ScenarioId, replay.Run!.ScenarioId);
        Assert.Equal(original.RunId, replay.Run.ReplayOf);
        Assert.False((await rig.Scenarios.ReplayAsync(Guid.NewGuid(), Instructor, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task DetectionsAndIncidentsAreAttributedToTheRun()
    {
        var rig = new TestRig();
        var run = await rig.RunScenarioAsync("command-replay");

        var incidents = await rig.Incidents.QueryAsync(new global::Application.Incidents.IncidentQuery(ScenarioRunId: run.RunId), CancellationToken.None);
        Assert.NotEqual(0, incidents.TotalCount);
        Assert.Contains(run.Actions, a => a.Action.StartsWith("Replay captured", StringComparison.Ordinal) && a.Detail.Contains("4 rejected"));
        Assert.True(run.Measurements["actionsExecuted"] >= 5);
    }

    [Fact]
    public async Task ResetAbortsTheActiveRunAndClearsTheCurrentView()
    {
        var rig = new TestRig();
        await rig.Scenarios.StartAsync("register-tampering", Instructor, CancellationToken.None);
        await rig.RunForAsync(12);

        Assert.True((await rig.Scenarios.ResetAsync(Instructor, CancellationToken.None)).Succeeded);

        Assert.Null(rig.Scenarios.Current);
        var runs = await rig.Scenarios.ListRunsAsync(10, CancellationToken.None);
        Assert.Equal(ScenarioOutcome.Aborted, Assert.Single(runs).Outcome);
        Assert.Equal(0, rig.Engine.Read(w => w.FindController("PLC-1")!.Snapshot().ForcedRegisterCount));
    }

    [Theory]
    [InlineData(ScenarioRunState.Completed, new[] { "a", "b" }, new[] { "a", "b", "c" }, ScenarioOutcome.Detected)]
    [InlineData(ScenarioRunState.Completed, new[] { "a", "b" }, new[] { "b" }, ScenarioOutcome.PartiallyDetected)]
    [InlineData(ScenarioRunState.Completed, new[] { "a" }, new string[0], ScenarioOutcome.Missed)]
    [InlineData(ScenarioRunState.Stopped, new[] { "a" }, new[] { "a" }, ScenarioOutcome.Aborted)]
    [InlineData(ScenarioRunState.Completed, new string[0], new string[0], ScenarioOutcome.Detected)]
    public Task OutcomeIsScoredFromExpectedAndObservedRules(ScenarioRunState state, string[] expected, string[] observed, ScenarioOutcome outcome)
    {
        Assert.Equal(outcome, ScenarioOutcomeEvaluator.Evaluate(state, expected, observed));
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidDefinitionsAreRejected()
    {
        var rig = new TestRig();
        var bad = new ScenarioDefinition("x", "X", "c", "d", 10, [], [new ScheduledScenarioAction(20, new global::Security.Scenarios.Actions.SpoofSensorAction("LT-101", 1))]);

        Assert.NotEmpty(bad.Validate());
        Assert.Throws<ArgumentException>(() => new ScenarioEngine([bad], null!, rig.Engine, rig.DetectionStore, rig.RunStore, rig.Events,
            new global::Infrastructure.Realtime.NullRealtimePublisher(), rig.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScenarioEngine>.Instance));
        return Task.CompletedTask;
    }
}
