using Application.Events;
using Application.Telemetry;
using Domain.Incidents;
using Infrastructure.Persistence.InMemory;
using Xunit;

namespace UnitTests.Infrastructure.Persistence;

public sealed class InMemoryStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EventStoreFiltersAndPagesNewestFirst()
    {
        var store = new InMemoryEventStore();
        var events = Enumerable.Range(0, 30).Select(i => SecurityEvent.Create(Start.AddSeconds(i),
            i % 3 == 0 ? EventCategories.SecurityAlert : EventCategories.Audit, EventSeverity.Low, "test", $"event {i}", i % 2 == 0 ? "ana" : "bob")).ToArray();

        Assert.Equal(30, await store.AppendAsync(events, CancellationToken.None));
        var alerts = await store.QueryAsync(new EventQuery(Category: "securityalert", PageSize: 4, Page: 1), CancellationToken.None);
        var ana = await store.QueryAsync(new EventQuery(Actor: "ana", From: Start.AddSeconds(20)), CancellationToken.None);

        Assert.Equal(10, alerts.TotalCount);
        Assert.Equal("event 27", alerts.Items[0].Message);
        Assert.Equal(3, alerts.TotalPages);
        Assert.Equal(5, ana.TotalCount);
    }

    [Fact]
    public async Task EventStoreEvictsOldestBeyondCapacityAndPurgesByAge()
    {
        var store = new InMemoryEventStore(capacity: 5);
        await store.AppendAsync(Enumerable.Range(0, 8).Select(i => SecurityEvent.Create(Start.AddMinutes(i), "Audit",
            EventSeverity.Low, "t", $"e{i}", "a")).ToArray(), CancellationToken.None);

        Assert.Equal(5, (await store.QueryAsync(new EventQuery(), CancellationToken.None)).TotalCount);
        Assert.Equal(2, await store.PurgeAsync(Start.AddMinutes(5), CancellationToken.None));
        Assert.Equal(3, (await store.QueryAsync(new EventQuery(), CancellationToken.None)).TotalCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public Task TelemetryQueryLimitIsValidated(int limit)
    {
        var store = new InMemoryTelemetryStore();
        return Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.QueryAsync(new TelemetryQuery(null, null, null, limit), CancellationToken.None));
    }

    [Fact]
    public async Task TelemetryQueryReturnsTheNewestSamplesInChronologicalOrder()
    {
        var store = new InMemoryTelemetryStore();
        await store.AppendAsync(Enumerable.Range(1, 10).Select(i => new TelemetrySample(i, Start.AddSeconds(i), TimeSpan.FromSeconds(i),
            i % 2 == 0 ? "A" : "B", i, "Good", "h")).ToArray(), CancellationToken.None);

        var result = await store.QueryAsync(new TelemetryQuery("A", null, null, 3), CancellationToken.None);

        Assert.Equal([6L, 8L, 10L], result.Select(s => s.Sequence));
    }

    [Fact]
    public async Task IncidentStoreNumbersIncidentsAndEnforcesVersions()
    {
        var store = new InMemoryIncidentStore();
        var incident = UnitTests.Domain.Incidents.IncidentWorkflowTests.NewIncident();

        var first = await store.AddAsync(incident, [], CancellationToken.None);
        var second = await store.AddAsync(incident with { Id = Guid.NewGuid() }, [], CancellationToken.None);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.True(await store.UpdateAsync(first with { Title = "Updated", Version = 2 }, 1, [], CancellationToken.None));
        Assert.False(await store.UpdateAsync(first with { Title = "Lost update", Version = 2 }, 1, [], CancellationToken.None));
        Assert.Equal("Updated", (await store.GetAsync(first.Id, CancellationToken.None))!.Title);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(first, [], CancellationToken.None));
    }

    [Fact]
    public async Task IncidentStoreFindsOnlyActiveCorrelatedIncidents()
    {
        var store = new InMemoryIncidentStore();
        var open = await store.AddAsync(UnitTests.Domain.Incidents.IncidentWorkflowTests.NewIncident() with { CorrelationKey = "k" }, [], CancellationToken.None);
        await store.AddAsync(UnitTests.Domain.Incidents.IncidentWorkflowTests.NewIncident() with
        {
            Id = Guid.NewGuid(),
            CorrelationKey = "k",
            Status = IncidentStatus.Resolved
        }, [], CancellationToken.None);

        Assert.Equal(open.Id, (await store.FindActiveByCorrelationAsync("k", CancellationToken.None))!.Id);
        Assert.Null(await store.FindActiveByCorrelationAsync("other", CancellationToken.None));
    }
}
