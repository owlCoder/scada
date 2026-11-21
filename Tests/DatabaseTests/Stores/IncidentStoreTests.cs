using Application.Incidents;
using DatabaseTests.Support;
using Domain.Incidents;
using Infrastructure.Persistence.Postgres.Stores;
using Xunit;

namespace DatabaseTests.Stores;

[Collection(PostgresCollection.Name)]
public sealed class IncidentStoreTests(PostgresContainerFixture postgres)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task IncidentsAreNumberedSequentiallyAndRoundTrip()
    {
        var store = new PostgresIncidentStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var first = Samples.Incident();

        var stored = await store.AddAsync(first, [Samples.Created(first)], None);
        var second = await store.AddAsync(Samples.Incident("Second"), [], None);

        Assert.Equal(1, stored.Number);
        Assert.Equal(2, second.Number);
        var loaded = await store.GetAsync(first.Id, None);
        Assert.NotNull(loaded);
        Assert.Equal("INC-00001", loaded.DisplayId);
        Assert.Equal(first.Title, loaded.Title);
        Assert.Equal(first.DeviceIds, loaded.DeviceIds);
        Assert.Equal(first.Evidence, loaded.Evidence);
        Assert.Equal(first.CreatedAt, loaded.CreatedAt);
        Assert.Single(await store.GetTimelineAsync(first.Id, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(first, [], None));
    }

    [Fact]
    public async Task UpdatesRequireTheCurrentVersion()
    {
        var store = new PostgresIncidentStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var incident = await store.AddAsync(Samples.Incident(), [], None);
        var acknowledged = incident with { Status = IncidentStatus.Acknowledged, AcknowledgedBy = "ana", Version = 2 };
        var entry = new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, Samples.Start.AddMinutes(1), "ana",
            IncidentTimelineEntryKind.StatusChanged, "Acknowledged", IncidentStatus.Open, IncidentStatus.Acknowledged);

        Assert.True(await store.UpdateAsync(acknowledged, expectedVersion: 1, [entry], None));
        Assert.False(await store.UpdateAsync(acknowledged with { Version = 2 }, expectedVersion: 1, [], None));
        Assert.False(await store.UpdateAsync(Samples.Incident(), expectedVersion: 1, [], None));

        var loaded = await store.GetAsync(incident.Id, None);
        Assert.Equal(IncidentStatus.Acknowledged, loaded!.Status);
        Assert.Equal(2, loaded.Version);
        Assert.Equal(1, loaded.Number);
        Assert.Single(await store.GetTimelineAsync(incident.Id, None));
    }

    [Fact]
    public async Task ConcurrentUpdatesWithTheSameVersionLetOnlyOneWin()
    {
        var store = new PostgresIncidentStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var incident = await store.AddAsync(Samples.Incident(), [], None);

        var attempts = Enumerable.Range(0, 8).Select(i =>
            store.UpdateAsync(incident with { AssignedTo = $"user-{i}", Version = 2 }, expectedVersion: 1, [], None));
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(2, (await store.GetAsync(incident.Id, None))!.Version);
    }

    [Fact]
    public async Task QueriesFilterSortSearchAndPageInTheDatabase()
    {
        var store = new PostgresIncidentStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        await store.AddAsync(Samples.Incident("Low level", IncidentSeverity.Low, devices: ["T-102"], minutes: 1), [], None);
        await store.AddAsync(Samples.Incident("100% overflow", IncidentSeverity.Critical, minutes: 2), [], None);
        await store.AddAsync(Samples.Incident("Old pump fault", IncidentSeverity.Medium, IncidentStatus.Resolved, ["P-101"], minutes: 3), [], None);

        var active = await store.QueryAsync(new IncidentQuery(ActiveOnly: true), None);
        Assert.Equal(2, active.TotalCount);
        Assert.Equal(["100% overflow", "Low level"], active.Items.Select(i => i.Title));

        var bySeverity = await store.QueryAsync(new IncidentQuery(SortBy: IncidentSortField.Severity), None);
        Assert.Equal([IncidentSeverity.Critical, IncidentSeverity.Medium, IncidentSeverity.Low], bySeverity.Items.Select(i => i.Severity));

        Assert.Equal("Old pump fault", (await store.QueryAsync(new IncidentQuery(DeviceId: "p-101"), None)).Items.Single().Title);
        Assert.Equal("Low level", (await store.QueryAsync(new IncidentQuery(Search: "inc-00001"), None)).Items.Single().Title);
        Assert.Equal("100% overflow", (await store.QueryAsync(new IncidentQuery(Search: "0% o"), None)).Items.Single().Title);
        Assert.Equal("100% overflow", (await store.QueryAsync(new IncidentQuery(Search: "%"), None)).Items.Single().Title);

        var page = await store.QueryAsync(new IncidentQuery(SortBy: IncidentSortField.Number, Descending: false, Page: 2, PageSize: 2), None);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Single().Number);

        var correlated = await store.FindActiveByCorrelationAsync("rule:tank", None);
        Assert.Equal("100% overflow", correlated!.Title);
    }

    [Fact]
    public async Task CommentsAreStoredWithATimelineEntry()
    {
        var store = new PostgresIncidentStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        var incident = await store.AddAsync(Samples.Incident(), [], None);
        var comment = new IncidentComment(Guid.NewGuid(), incident.Id, "ana", "Checked the level gauge", Samples.Start.AddMinutes(5));
        var entry = new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, comment.CreatedAt, "ana", IncidentTimelineEntryKind.Commented,
            "Comment added", null, null);

        Assert.True(await store.AddCommentAsync(comment, entry, None));
        Assert.False(await store.AddCommentAsync(comment with { Id = Guid.NewGuid(), IncidentId = Guid.NewGuid() }, entry with { Id = Guid.NewGuid() }, None));

        Assert.Equal(comment, (await store.GetCommentsAsync(incident.Id, None)).Single());
        Assert.Equal(IncidentTimelineEntryKind.Commented, (await store.GetTimelineAsync(incident.Id, None)).Single().Kind);
    }
}
