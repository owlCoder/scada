using Application.Alarms;
using Application.Detection;
using Application.Events;
using Application.Telemetry;
using DatabaseTests.Support;
using Domain.Incidents;
using Domain.Process.Alarms;
using Infrastructure.Persistence.Postgres.Stores;
using Xunit;

namespace DatabaseTests.Stores;

[Collection(PostgresCollection.Name)]
public sealed class EventAndTelemetryStoreTests(PostgresContainerFixture postgres)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EventsArePagedNewestFirstAndFilteredLiterally()
    {
        var store = new PostgresEventStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        await store.AppendAsync([
            Samples.Event("Pump started", seconds: 1),
            Samples.Event("Valve 50% open", seconds: 2),
            Samples.Event("Valve 50 open", seconds: 3),
            Samples.Event("Invalid key", "Authentication", 4, "unknown")
        ], None);

        var page = await store.QueryAsync(new EventQuery(PageSize: 2), None);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(["Invalid key", "Valve 50 open"], page.Items.Select(e => e.Message));

        Assert.Equal("Valve 50% open", (await store.QueryAsync(new EventQuery(Search: "50%"), None)).Items.Single().Message);
        Assert.Equal("Invalid key", (await store.QueryAsync(new EventQuery(Category: "authentication"), None)).Items.Single().Message);
        Assert.Equal(3, (await store.QueryAsync(new EventQuery(Actor: "ANA"), None)).TotalCount);
        var window = await store.QueryAsync(new EventQuery(From: Samples.Start.AddSeconds(2), To: Samples.Start.AddSeconds(3)), None);
        Assert.Equal(2, window.TotalCount);
    }

    [Fact]
    public async Task TelemetryReturnsTheNewestSamplesInChronologicalOrder()
    {
        var store = new PostgresTelemetryStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var samples = Enumerable.Range(1, 10).Select(i =>
            new TelemetrySample(i, Samples.Start.AddSeconds(i), TimeSpan.FromSeconds(i * 5), i % 2 == 0 ? "LT-101" : "FT-101", i, "Good", $"h{i}"))
            .ToArray();
        Assert.Equal(10, await store.AppendAsync(samples, None));

        var latest = await store.QueryAsync(new TelemetryQuery("LT-101", null, null, Limit: 3), None);

        Assert.Equal([6.0, 8.0, 10.0], latest.Select(s => s.Value));
        Assert.Equal(samples[9], latest[^1]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.QueryAsync(new TelemetryQuery(null, null, null, 0), None));
        Assert.Equal(4, await store.PurgeAsync(Samples.Start.AddSeconds(5), None));
        Assert.Equal(6, (await store.QueryAsync(new TelemetryQuery(null, null, null), None)).Count);
    }

    [Fact]
    public async Task AlarmHistoryAndDetectionsRoundTrip()
    {
        var factory = await (await postgres.CreateDatabaseAsync()).MigratedAsync();
        var alarms = new PostgresAlarmHistoryStore(factory);
        var raised = new AlarmHistoryRecord(Guid.NewGuid(), Samples.Start, "T-101:High", "T-101", AlarmType.High, AlarmSeverity.High,
            AlarmTransitionKind.Raised, "Level high", null);
        await alarms.AppendAsync([raised, raised with { Id = Guid.NewGuid(), Timestamp = Samples.Start.AddSeconds(9), Transition = AlarmTransitionKind.Acknowledged, Actor = "ana" }], None);
        var recent = await alarms.GetRecentAsync(10, None);
        Assert.Equal([AlarmTransitionKind.Acknowledged, AlarmTransitionKind.Raised], recent.Select(a => a.Transition));
        Assert.Equal(raised, recent[1]);

        var detections = new PostgresDetectionStore(factory);
        var run = Guid.NewGuid();
        var detection = new DetectionRecord(Guid.NewGuid(), Samples.Start, "command-replay", "Command replay", DetectionSeverity.High,
            IncidentCategory.Cybersecurity, "Replay", "Replayed command", "P-101", run, "command-replay:P-101:run",
            [new DetectionEvidence("event", "abc", "Sequence reused")], null);
        Assert.True(await detections.AppendAsync(detection, None));
        await detections.AppendAsync(detection with { Id = Guid.NewGuid(), RuleId = "mass-balance", ScenarioRunId = null }, None);

        var forRun = await detections.QueryAsync(new DetectionQuery(ScenarioRunId: run), None);
        var stored = Assert.Single(forRun);
        Assert.Equal(detection.Evidence, stored.Evidence);
        Assert.Equal(detection with { Evidence = stored.Evidence }, stored);
        Assert.Single(await detections.QueryAsync(new DetectionQuery(RuleId: "mass-balance"), None));
    }
}
