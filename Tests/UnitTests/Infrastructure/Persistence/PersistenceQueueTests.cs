using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Simulation;
using Application.Telemetry;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Infrastructure.Persistence;

public sealed class PersistenceQueueTests
{
    private static SecurityEventWorkItem Item(string message) =>
        new(SecurityEvent.Create(DateTimeOffset.UnixEpoch, EventCategories.Audit, EventSeverity.Information, "test", message, "tester"));

    [Fact]
    public Task FullQueueDropsAndCountsInsteadOfBlocking()
    {
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = 2 }, new FakeTimeProvider());

        Assert.True(queue.TryEnqueue(Item("a")));
        Assert.True(queue.TryEnqueue(Item("b")));
        Assert.False(queue.TryEnqueue(Item("c")));

        var status = queue.Status;
        Assert.Equal(2, status.Pending);
        Assert.Equal(2, status.Enqueued);
        Assert.Equal(1, status.Dropped);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WorkerPersistsEveryKindOfItem()
    {
        var events = new InMemoryEventStore();
        var telemetry = new InMemoryTelemetryStore();
        var (queue, worker) = Create(events, telemetry, new InMemoryAlarmHistoryStore());
        queue.TryEnqueue(Item("persist me"));
        queue.TryEnqueue(new TelemetryBatchWorkItem([new TelemetrySample(1, DateTimeOffset.UnixEpoch, TimeSpan.Zero, "LT-101", 50, "Good", "h")]));

        Assert.Equal(2, await worker.DrainAsync(CancellationToken.None));

        Assert.Equal(1, (await events.QueryAsync(new EventQuery(Search: "persist me"), CancellationToken.None)).TotalCount);
        Assert.Single(await telemetry.QueryAsync(new TelemetryQuery("LT-101", null, null, 10), CancellationToken.None));
        Assert.Equal(2, queue.Status.Persisted);
    }

    [Fact]
    public async Task FailingStoreIsRetriedThenCountedAsFailed()
    {
        var failing = new FailingEventStore();
        var (queue, worker) = Create(failing, new InMemoryTelemetryStore(), new InMemoryAlarmHistoryStore());
        queue.TryEnqueue(Item("x"));

        await worker.DrainAsync(CancellationToken.None);

        Assert.Equal(3, failing.Attempts);
        Assert.Equal(1, queue.Status.Failed);
        Assert.Equal(0, queue.Status.Persisted);
        Assert.Equal("database unavailable", queue.Status.LastError);
    }

    [Fact]
    public Task InvalidOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = 0 }, new FakeTimeProvider()));
        Assert.NotEmpty(new PersistenceQueueOptions { MaxAttempts = 0 }.Validate());
        return Task.CompletedTask;
    }

    private static (ChannelPersistenceQueue Queue, PersistenceWorker Worker) Create(IEventStore events, ITelemetryStore telemetry,
        IAlarmHistoryStore alarms)
    {
        var options = new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero };
        var queue = new ChannelPersistenceQueue(options, new FakeTimeProvider());
        var worker = new PersistenceWorker(queue, new PersistenceDispatcher(events, telemetry, alarms, new InMemoryPlantStateStore()),
            options, NullLogger<PersistenceWorker>.Instance);
        return (queue, worker);
    }
}
