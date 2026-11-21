using Application.Incidents;
using Application.Telemetry;
using DatabaseTests.Support;
using Domain.Incidents;
using Infrastructure.Persistence.Postgres.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DatabaseTests.Migrations;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task MigrationsCreateTheSchemaAndMatchTheModel()
    {
        var database = await postgres.CreateDatabaseAsync();

        var applied = await database.MigrateAsync();

        Assert.Contains(applied, m => m.EndsWith("_InitialSchema", StringComparison.Ordinal));
        await using var db = await database.ContextFactory().CreateDbContextAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The EF model has changes without a migration. Run dotnet ef migrations add.");
        foreach (var table in new[] { "events", "telemetry", "alarm_history", "plant_state", "incidents", "incident_timeline",
                     "incident_comments", "detections", "detection_rule_settings", "scenario_runs", "user_accounts" })
            Assert.Equal(1, await database.ScalarAsync<int>($"SELECT count(*) FROM pg_tables WHERE schemaname = 'scada' AND tablename = '{table}'"));
    }

    [Fact]
    public async Task MigratingTwiceIsANoOp()
    {
        var database = await postgres.CreateDatabaseAsync();
        await database.MigrateAsync();

        Assert.Empty(await database.MigrateAsync());
    }

    [Fact]
    public async Task LegacyTablesAreCopiedAndKept()
    {
        var database = await postgres.CreateDatabaseAsync();
        await database.ExecuteAsync(RepositoryFiles.Read("deploy", "postgres", "init.sql"));
        await database.ExecuteAsync("""
            INSERT INTO security_events (occurred_at, category, message, actor) VALUES
                ('2025-03-01T10:00:00Z', 'Command', 'Pump started', 'operator'),
                ('2025-03-01T10:05:00Z', 'Security', 'Invalid API key', 'unknown');
            INSERT INTO telemetry_samples (recorded_at, tank_level, pump_running, revision) VALUES
                ('2025-03-01T10:00:00Z', 41.5, true, 1),
                ('2025-03-01T10:00:01Z', 42.0, false, 2);
            INSERT INTO incidents (id, created_at, category, description, actor, status, updated_by, updated_at) VALUES
                ('11111111-1111-1111-1111-111111111111', '2025-03-01T10:00:00Z', 'Security', 'Brute force attempt', 'unknown', 0, NULL, NULL),
                ('22222222-2222-2222-2222-222222222222', '2025-03-01T11:00:00Z', 'Command', 'Unexpected pump stop', 'operator', 1, 'bob', '2025-03-01T11:10:00Z'),
                ('33333333-3333-3333-3333-333333333333', '2025-03-01T12:00:00Z', 'Equipment', 'Valve stuck', 'operator', 2, 'eva', '2025-03-01T12:30:00Z');
            """);

        await database.MigrateAsync();

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT count(*) FROM scada.events WHERE source = 'legacy-import'"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT count(*) FROM scada.events WHERE message = 'Invalid API key' AND actor = 'unknown'"));

        var factory = database.ContextFactory();
        var level = await new PostgresTelemetryStore(factory).QueryAsync(new TelemetryQuery("LT-101", null, null), CancellationToken.None);
        Assert.Equal([41.5, 42.0], level.Select(s => s.Value));
        var pump = await new PostgresTelemetryStore(factory).QueryAsync(new TelemetryQuery("P-101.running", null, null), CancellationToken.None);
        Assert.Equal([1.0, 0.0], pump.Select(s => s.Value));

        var incidents = new PostgresIncidentStore(factory);
        var all = await incidents.QueryAsync(new IncidentQuery(SortBy: IncidentSortField.Number, Descending: false), CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], all.Items.Select(i => i.Number));
        Assert.Equal([IncidentStatus.Open, IncidentStatus.Acknowledged, IncidentStatus.Resolved], all.Items.Select(i => i.Status));
        Assert.Equal([IncidentCategory.Cybersecurity, IncidentCategory.Operational, IncidentCategory.Equipment], all.Items.Select(i => i.Category));
        Assert.Equal("bob", all.Items[1].AcknowledgedBy);
        Assert.Equal("eva", all.Items[2].ResolvedBy);
        Assert.Single(await incidents.GetTimelineAsync(all.Items[0].Id, CancellationToken.None));

        // Imported incidents keep numbering continuous for new ones.
        var created = await incidents.AddAsync(NewIncident(), [], CancellationToken.None);
        Assert.Equal(4, created.Number);

        // The legacy tables are untouched.
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT count(*) FROM public.security_events"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT count(*) FROM public.telemetry_samples"));
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT count(*) FROM public.incidents"));
    }

    [Fact]
    public async Task DatabaseWithoutLegacyTablesMigratesWithoutImporting()
    {
        var database = await postgres.CreateDatabaseAsync();

        await database.MigrateAsync();

        Assert.Equal(0, await database.ScalarAsync<int>("SELECT count(*) FROM scada.events"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT count(*) FROM pg_tables WHERE schemaname = 'public'"));
    }

    private static Incident NewIncident() => new(Guid.NewGuid(), 0, "New", "After import", IncidentCategory.Operational,
        IncidentSeverity.Low, IncidentPriority.Normal, "test", IncidentStatus.Open, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        null, null, null, null, null, [], null, "new", [], 1);
}
