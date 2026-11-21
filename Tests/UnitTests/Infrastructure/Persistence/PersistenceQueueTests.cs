using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Simulation;
using Application.Telemetry;
using Domain.Process.Alarms;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
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
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = 2 }, new FakeTimeProvider(), NullLogger<ChannelPersistenceQueue>.Instance);

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
    public async Task RetryingTelemetryLeavesEventsAndAlarmsPersistedExactlyOnce()
    {
        var events = new InMemoryEventStore();
        var telemetry = new TransientTelemetryStore();
        var alarms = new InMemoryAlarmHistoryStore();
        var (queue, worker) = Create(events, telemetry, alarms);
        var alarm = new AlarmHistoryRecord(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "T-101:High", "T-101", AlarmType.High,
            AlarmSeverity.High, AlarmTransitionKind.Raised, "Level high", null);
        queue.TryEnqueue(Item("persist me"));
        queue.TryEnqueue(new TelemetryBatchWorkItem([new TelemetrySample(1, DateTimeOffset.UnixEpoch, TimeSpan.Zero, "LT-101", 50, "Good", "h")]));
        queue.TryEnqueue(new AlarmHistoryWorkItem(alarm));

        Assert.Equal(3, await worker.DrainAsync(CancellationToken.None));

        Assert.Equal(2, telemetry.Attempts);
        Assert.Equal(1, (await events.QueryAsync(new EventQuery(), CancellationToken.None)).TotalCount);
        Assert.Single(await telemetry.QueryAsync(new TelemetryQuery(null, null, null), CancellationToken.None));
        Assert.Equal(alarm, Assert.Single(await alarms.GetRecentAsync(10, CancellationToken.None)));
        Assert.Equal(3, queue.Status.Persisted);
        Assert.Equal(0, queue.Status.Failed);
    }

    [Fact]
    public async Task AKindThatExhaustsItsRetriesDoesNotPreventAnotherKindFromPersisting()
    {
        var events = new FailingEventStore();
        var telemetry = new InMemoryTelemetryStore();
        var (queue, worker) = Create(events, telemetry, new InMemoryAlarmHistoryStore());
        queue.TryEnqueue(Item("fails"));
        queue.TryEnqueue(new TelemetryBatchWorkItem([new TelemetrySample(1, DateTimeOffset.UnixEpoch, TimeSpan.Zero, "LT-101", 50, "Good", "h")]));

        Assert.Equal(2, await worker.DrainAsync(CancellationToken.None));

        Assert.Equal(3, events.Attempts);
        Assert.Single(await telemetry.QueryAsync(new TelemetryQuery(null, null, null), CancellationToken.None));
        Assert.Equal(1, queue.Status.Persisted);
        Assert.Equal(1, queue.Status.Failed);
    }

    [Fact]
    public async Task ConcurrentDropsAreLoggedAtMostOncePerMinute()
    {
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<ChannelPersistenceQueue>();
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = 1 }, time, logger);
        Assert.True(queue.TryEnqueue(Item("held")));

        var rejected = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => queue.TryEnqueue(Item("dropped")))));
        Assert.All(rejected, Assert.False);
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(queue.TryEnqueue(Item("quiet drop")));
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Collector.GetSnapshot()).Level);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(queue.TryEnqueue(Item("another drop")));
        Assert.Equal(2, logger.Collector.Count);
        Assert.Equal(22, queue.Status.Dropped);
        Assert.Equal(1, queue.Status.Pending);
    }

    [Fact]
    public Task InvalidOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = 0 }, new FakeTimeProvider(), NullLogger<ChannelPersistenceQueue>.Instance));
        Assert.NotEmpty(new PersistenceQueueOptions { MaxAttempts = 0 }.Validate());
        return Task.CompletedTask;
    }

    private static (ChannelPersistenceQueue Queue, PersistenceWorker Worker) Create(IEventStore events, ITelemetryStore telemetry,
        IAlarmHistoryStore alarms)
    {
        var options = new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero };
        var time = new FakeTimeProvider();
        var queue = new ChannelPersistenceQueue(options, time, NullLogger<ChannelPersistenceQueue>.Instance);
        var worker = new PersistenceWorker(queue, new PersistenceDispatcher(events, telemetry, alarms, new InMemoryPlantStateStore()),
            options, time, NullLogger<PersistenceWorker>.Instance);
        return (queue, worker);
    }
}
