using Application.Realtime;
using Infrastructure.Realtime;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace UnitTests.Infrastructure;

public sealed class RealtimeBroadcasterTests
{
    [Fact]
    public async Task SubscribersReceiveOnlyTheirTopics()
    {
        var broadcaster = new RealtimeBroadcaster(new FakeTimeProvider());
        await using var plant = broadcaster.Subscribe([RealtimeTopics.Plant]);
        await using var everything = broadcaster.Subscribe([]);

        broadcaster.Publish(RealtimeTopics.Plant, "p");
        broadcaster.Publish(RealtimeTopics.Events, "e");

        Assert.True(plant.Reader.TryRead(out var only));
        Assert.Equal("p", only.Payload);
        Assert.False(plant.Reader.TryRead(out _));
        Assert.Equal(2, everything.Reader.Count);
    }

    [Fact]
    public async Task ASlowSubscriberLosesItsOldestMessagesWithoutAffectingOthers()
    {
        var broadcaster = new RealtimeBroadcaster(new FakeTimeProvider());
        await using var slow = broadcaster.Subscribe([], capacity: 2);
        await using var fast = broadcaster.Subscribe([], capacity: 10);

        for (var i = 0; i < 5; i++)
            Assert.True(broadcaster.Publish(RealtimeTopics.Plant, i));

        Assert.Equal(3, slow.Dropped);
        Assert.True(slow.Reader.TryRead(out var oldest));
        Assert.Equal(3, oldest.Payload);
        Assert.Equal(5, fast.Reader.Count);
        Assert.Equal(0, fast.Dropped);
    }

    [Fact]
    public async Task DisposingUnsubscribes()
    {
        var broadcaster = new RealtimeBroadcaster(new FakeTimeProvider());
        var subscription = broadcaster.Subscribe([]);
        Assert.Equal(1, broadcaster.SubscriberCount);

        await subscription.DisposeAsync();

        Assert.Equal(0, broadcaster.SubscriberCount);
        Assert.False(broadcaster.Publish(RealtimeTopics.Plant, "nobody listens"));
        Assert.True(subscription.Reader.Completion.IsCompleted);
    }
}
