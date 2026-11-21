using System.Diagnostics;
using Application.Realtime;
using Infrastructure.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTests.Realtime;

[Trait("Category", "Performance")]
public sealed class RealtimePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FanOutTo100SubscribersNeverWaitsForStalledReaderAndDropsOldest()
    {
        var broadcaster = new RealtimeBroadcaster(TimeProvider.System);
        const int capacity = 8;
        const int count = 100;
        const int messages = 2000;
        var subscribers = Enumerable.Range(0, count).Select(_ => broadcaster.Subscribe([RealtimeTopics.Plant], capacity)).ToArray();
        try
        {
            Assert.Equal(count, broadcaster.SubscriberCount);
            var delivered = new int[count - 1];
            var watch = Stopwatch.StartNew();
            for (var sequence = 0; sequence < messages; sequence++)
            {
                Assert.True(broadcaster.Publish(RealtimeTopics.Plant, sequence));
                // Reader 0 remains stalled; the other 99 keep up deterministically without thread scheduling noise.
                for (var index = 1; index < count; index++)
                {
                    Assert.True(subscribers[index].Reader.TryRead(out var message));
                    Assert.Equal(sequence, Assert.IsType<int>(message.Payload));
                    delivered[index - 1]++;
                }
            }
            watch.Stop();
            output.WriteLine("Realtime: {0} publications to {1} subscribers in {2:F1} ms; {3:F0} deliveries/s; stalled drops {4}.",
                messages, count, watch.Elapsed.TotalMilliseconds, messages * count / watch.Elapsed.TotalSeconds, subscribers[0].Dropped);
            Assert.All(delivered, received => Assert.Equal(messages, received));
            Assert.All(subscribers.Skip(1), subscription => Assert.Equal(0, subscription.Dropped));
            Assert.Equal(messages - capacity, subscribers[0].Dropped);
            var retained = new List<int>();
            while (subscribers[0].Reader.TryRead(out var message))
                retained.Add(Assert.IsType<int>(message.Payload));
            Assert.Equal(Enumerable.Range(messages - capacity, capacity), retained);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Realtime fan-out took {watch.Elapsed}.");
        }
        finally
        {
            foreach (var subscriber in subscribers)
                await subscriber.DisposeAsync();
        }
        Assert.Equal(0, broadcaster.SubscriberCount);
    }
}
