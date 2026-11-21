using System.Diagnostics;
using Application.Events;
using Application.Persistence;
using Infrastructure.Persistence.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTests.Persistence;

[Trait("Category", "Performance")]
public sealed class PersistencePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public Task QueueThroughputRemainsBoundedUnderOverload()
    {
        const int capacity = 256;
        const int attempts = 100_000;
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { Capacity = capacity }, TimeProvider.System,
            NullLogger<ChannelPersistenceQueue>.Instance);
        var item = new SecurityEventWorkItem(SecurityEvent.Create(TimeProvider.System.GetUtcNow(), "performance",
            EventSeverity.Information, "test", "queue throughput", "performance"));
        // Exercise the accepting and draining path before measuring overload.
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < attempts; i++)
        {
            Assert.True(queue.TryEnqueue(item));
            Assert.True(queue.TryDequeue(out var dequeued));
            Assert.Same(item, dequeued);
        }
        watch.Stop();
        var throughput = attempts / watch.Elapsed.TotalSeconds;
        output.WriteLine("Persistence queue: {0} enqueue/dequeue pairs in {1:F1} ms; {2:F0} items/s.",
            attempts, watch.Elapsed.TotalMilliseconds, throughput);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Queue throughput took {watch.Elapsed}.");
        // Distinct payloads make retained-memory growth observable if the queue accidentally becomes unbounded.
        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        watch.Restart();
        for (var i = 0; i < attempts; i++)
            queue.TryEnqueue(item with { Event = item.Event with { Message = new string('x', 512) } });
        watch.Stop();
        var retainedGrowth = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;
        output.WriteLine("Persistence overload: {0} attempts in {1:F1} ms; pending {2}/{3}, dropped {4}, retained growth {5:N0} bytes.",
            attempts, watch.Elapsed.TotalMilliseconds, queue.Status.Pending, capacity, queue.Status.Dropped, retainedGrowth);
        Assert.Equal(capacity, queue.Status.Pending);
        Assert.Equal(attempts - capacity, queue.Status.Dropped);
        Assert.Equal(attempts + capacity, queue.Status.Enqueued);
        Assert.InRange(retainedGrowth, long.MinValue, 16 * 1024 * 1024);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Queue overload took {watch.Elapsed}.");
        var drained = 0;
        while (queue.TryDequeue(out _))
            drained++;
        Assert.Equal(capacity, drained);
        Assert.Equal(0, queue.Status.Pending);
        Assert.True(queue.Complete());
        GC.KeepAlive(queue);
        return Task.CompletedTask;
    }
}
