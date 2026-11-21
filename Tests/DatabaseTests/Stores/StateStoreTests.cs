using Application.Detection;
using Application.Identity;
using Application.Scenarios;
using Application.Simulation;
using DatabaseTests.Support;
using Infrastructure.Persistence.Postgres;
using Infrastructure.Persistence.Postgres.Stores;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace DatabaseTests.Stores;

[Collection(PostgresCollection.Name)]
public sealed class StateStoreTests(PostgresContainerFixture postgres)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task PlantStateRoundTripsAndRestoresAnEngine()
    {
        var store = new PostgresPlantStateStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var time = new FakeTimeProvider(Samples.Start);
        var engine = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), time);
        for (var i = 0; i < 40; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            engine.Step(force: true);
        }

        var state = engine.CaptureState();
        Assert.Null(await store.LoadLatestAsync(None));
        Assert.True(await store.SaveAsync(state, None));

        var loaded = await store.LoadLatestAsync(None);
        Assert.NotNull(loaded);
        Assert.Equal(PersistenceJson.Serialize(state), PersistenceJson.Serialize(loaded));
        var restored = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), new FakeTimeProvider(Samples.Start));
        Assert.True(restored.Restore(loaded));
        Assert.Equal(state.Plant.StepCount, restored.CaptureState().Plant.StepCount);
    }

    [Fact]
    public async Task OlderPlantStateNeverOverwritesNewer()
    {
        var store = new PostgresPlantStateStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var state = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), new FakeTimeProvider(Samples.Start)).CaptureState();

        Assert.True(await store.SaveAsync(state with { SavedAt = Samples.Start.AddMinutes(2), IsPaused = true }, None));
        Assert.False(await store.SaveAsync(state with { SavedAt = Samples.Start.AddMinutes(1) }, None));
        Assert.True(await store.SaveAsync(state with { SavedAt = Samples.Start.AddMinutes(3) }, None));

        var latest = await store.LoadLatestAsync(None);
        Assert.Equal(Samples.Start.AddMinutes(3), latest!.SavedAt);
        Assert.False(latest.IsPaused);
    }

    [Fact]
    public async Task OlderPlantStateWaitingOnAConcurrentWriterIsRejected()
    {
        var database = await postgres.CreateDatabaseAsync();
        var store = new PostgresPlantStateStore(await database.MigratedAsync());
        var state = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), new FakeTimeProvider(Samples.Start)).CaptureState();
        Assert.True(await store.SaveAsync(state, None));

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var newer = state.SavedAt.AddMinutes(2);
        await using var command = new NpgsqlCommand("UPDATE scada.plant_state SET saved_at = $1, is_paused = true", connection, transaction);
        command.Parameters.AddWithValue(newer);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var older = store.SaveAsync(state with { SavedAt = state.SavedAt.AddMinutes(1) }, timeout.Token);
        while (await database.ScalarAsync<int>("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%plant_state%'
            """) == 0)
            await Task.Delay(10, timeout.Token);

        await transaction.CommitAsync(timeout.Token);
        Assert.False(await older);
        var latest = (await store.LoadLatestAsync(None))!;
        Assert.Equal(newer, latest.SavedAt);
        Assert.True(latest.IsPaused);
    }

    [Fact]
    public async Task AccountsAreCaseInsensitiveAndUpserted()
    {
        var store = new PostgresUserAccountStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var ana = new UserAccount("Ana", "Ana Operator", AccessRole.Operator, "hash", "key-hash", true, Samples.Start, null, null, 0, null);

        Assert.True(await store.TryCreateAsync(ana, None));
        Assert.True(await store.TryUpdateAsync(ana with { Role = AccessRole.Viewer, LockedUntil = Samples.Start.AddMinutes(5) }, None));
        Assert.False(await store.TryCreateAsync(ana with { Username = "  " }, None));

        var found = await store.FindAsync(" ANA ", None);
        Assert.Equal(ana with { Role = AccessRole.Viewer, LockedUntil = Samples.Start.AddMinutes(5), Version = 1 }, found);
        Assert.Single(await store.ListAsync(None));
        Assert.Null(await store.FindAsync("", None));

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            var carl = ana with { Username = "carl", DisplayName = $"Carl {i}" };
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var current = await store.FindAsync(carl.Username, None);
                if (current is null ? await store.TryCreateAsync(carl, None)
                    : await store.TryUpdateAsync(current with { DisplayName = carl.DisplayName }, None))
                    return true;
            }

            return false;
        }));
        Assert.All(concurrent, Assert.True);
        Assert.Equal(2, (await store.ListAsync(None)).Count);
    }

    [Fact]
    public async Task RuleSettingsAndScenarioRunsAreUpserted()
    {
        var factory = await (await postgres.CreateDatabaseAsync()).MigratedAsync();
        var settings = new PostgresDetectionRuleSettingsStore(factory);
        var replay = new DetectionRuleSettings("command-replay", true, DetectionSeverity.High, 1, 60, 300);
        await settings.SaveAsync(replay, None);
        await settings.SaveAsync(replay with { Enabled = false, Threshold = 4 }, None);
        await settings.SaveAsync(new DetectionRuleSettings("a-rule", true, DetectionSeverity.Low, 2, 30, 30), None);
        var loaded = await settings.LoadAsync(None);
        Assert.Equal(["a-rule", "command-replay"], loaded.Select(s => s.RuleId));
        Assert.Equal(replay with { Enabled = false, Threshold = 4 }, loaded[1]);

        var runs = new PostgresScenarioRunStore(factory);
        var run = new ScenarioRunRecord(Guid.NewGuid(), "command-replay", "Command replay", "Cyber", ScenarioRunState.Running, "ana",
            Samples.Start, null, TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(60), 0,
            [new ScenarioActionLog(TimeSpan.FromSeconds(1), Samples.Start.AddSeconds(1), "Replay command", true, "ok")],
            new Dictionary<string, double> { ["commands"] = 3 }, ["command-replay"], [], ScenarioOutcome.Pending, null);
        await runs.SaveAsync(run, None);
        var finished = run with { State = ScenarioRunState.Completed, EndedAt = Samples.Start.AddMinutes(1), ObservedDetections = ["command-replay"],
            Outcome = ScenarioOutcome.Detected, ProgressPercent = 100 };
        await runs.SaveAsync(finished, None);
        await runs.SaveAsync(run with { RunId = Guid.NewGuid(), StartedAt = Samples.Start.AddMinutes(5) }, None);

        var stored = await runs.GetAsync(run.RunId, None);
        Assert.NotNull(stored);
        Assert.Equal(ScenarioOutcome.Detected, stored.Outcome);
        Assert.Equal(["command-replay"], stored.ObservedDetections);
        Assert.Equal(3, stored.Measurements["commands"]);
        Assert.Equal(run.Actions, stored.Actions);
        Assert.Equal(TimeSpan.FromSeconds(60), stored.Duration);
        var list = await runs.ListAsync(10, None);
        Assert.Equal(2, list.Count);
        Assert.Equal(run.RunId, list[1].RunId);
    }
}
