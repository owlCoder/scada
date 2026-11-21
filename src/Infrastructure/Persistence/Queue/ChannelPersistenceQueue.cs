using System.Threading.Channels;
using Application.Persistence;

namespace Infrastructure.Persistence.Queue;

/// <summary>
/// Bounded write-behind queue. <see cref="TryEnqueue"/> never blocks: when the queue is full the item is dropped and
/// counted. The <see cref="PersistenceWorker"/> drains it and reports the outcome of each item back.
/// </summary>
public sealed class ChannelPersistenceQueue : IPersistenceQueue
{
    private readonly Channel<PersistenceWorkItem> _channel;
    private readonly TimeProvider _time;
    private readonly Lock _sync = new();
    private long _enqueued;
    private long _persisted;
    private long _dropped;
    private long _failed;
    private DateTimeOffset? _lastFailureAt;
    private string? _lastError;

    public ChannelPersistenceQueue(PersistenceQueueOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));

        Capacity = options.Capacity;
        _time = time;
        _channel = Channel.CreateBounded<PersistenceWorkItem>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int Capacity { get; }

    public PersistenceQueueStatus Status
    {
        get
        {
            lock (_sync)
                return new PersistenceQueueStatus(Capacity, _channel.Reader.Count, _enqueued, _persisted, _dropped, _failed,
                    _lastFailureAt, _lastError);
        }
    }

    public bool TryEnqueue(PersistenceWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var written = _channel.Writer.TryWrite(item);
        lock (_sync)
        {
            if (written)
                _enqueued++;
            else
                _dropped++;
        }

        return written;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

    public bool TryDequeue(out PersistenceWorkItem? item)
    {
        var read = _channel.Reader.TryRead(out var value);
        item = value;
        return read;
    }

    public long MarkPersisted(int count)
    {
        lock (_sync)
            return _persisted += count;
    }

    public long MarkFailed(int count, string error)
    {
        lock (_sync)
        {
            _lastFailureAt = _time.GetUtcNow();
            _lastError = error;
            return _failed += count;
        }
    }

    /// <summary>Stops accepting new items so the worker can drain what is left during shutdown.</summary>
    public bool Complete() => _channel.Writer.TryComplete();
}
