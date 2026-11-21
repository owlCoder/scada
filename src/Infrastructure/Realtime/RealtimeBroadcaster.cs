using System.Threading.Channels;
using Application.Realtime;

namespace Infrastructure.Realtime;

/// <summary>
/// Fans realtime messages out to every subscriber's own bounded channel (DropOldest). Publishing never blocks, so a slow
/// browser or hub client can only lose its own oldest messages; it cannot stall the simulation loop.
/// </summary>
public sealed class RealtimeBroadcaster(TimeProvider time) : IRealtimePublisher, IRealtimeFeed
{
    private readonly Lock _sync = new();
    private Subscription[] _subscribers = [];

    public int SubscriberCount => Volatile.Read(ref _subscribers).Length;

    public bool Publish(string topic, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(payload);
        var message = new RealtimeMessage(topic, payload, time.GetUtcNow());
        var delivered = false;
        foreach (var subscriber in Volatile.Read(ref _subscribers))
            delivered |= subscriber.Offer(message);
        return delivered;
    }

    public IRealtimeSubscription Subscribe(IReadOnlyCollection<string> topics, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var subscription = new Subscription(this, topics, capacity);
        lock (_sync)
            _subscribers = [.. _subscribers, subscription];
        return subscription;
    }

    private bool Remove(Subscription subscription)
    {
        lock (_sync)
        {
            var remaining = _subscribers.Where(s => !ReferenceEquals(s, subscription)).ToArray();
            var removed = remaining.Length != _subscribers.Length;
            _subscribers = remaining;
            return removed;
        }
    }

    private sealed class Subscription : IRealtimeSubscription
    {
        private readonly RealtimeBroadcaster _owner;
        private readonly HashSet<string>? _topics;
        private readonly Channel<RealtimeMessage> _channel;
        private long _dropped;

        public Subscription(RealtimeBroadcaster owner, IReadOnlyCollection<string> topics, int capacity)
        {
            _owner = owner;
            _topics = topics.Count == 0 ? null : topics.ToHashSet(StringComparer.Ordinal);
            _channel = Channel.CreateBounded<RealtimeMessage>(
                new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => Interlocked.Increment(ref _dropped));
        }

        public ChannelReader<RealtimeMessage> Reader => _channel.Reader;
        public long Dropped => Interlocked.Read(ref _dropped);

        public bool Offer(RealtimeMessage message) =>
            (_topics is null || _topics.Contains(message.Topic)) && _channel.Writer.TryWrite(message);

        public ValueTask DisposeAsync()
        {
            _owner.Remove(this);
            _channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
