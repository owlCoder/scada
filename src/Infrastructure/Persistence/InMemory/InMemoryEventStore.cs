using Application.Common;
using Application.Events;
using Application.Persistence;

namespace Infrastructure.Persistence.InMemory;

/// <summary>Bounded in-memory event store. Oldest events are evicted once <see cref="Capacity"/> is reached.</summary>
public sealed class InMemoryEventStore(int capacity = 50_000) : IEventStore, IRetentionTarget
{
    private readonly Lock _sync = new();
    private readonly LinkedList<SecurityEvent> _events = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public string Name => "events";

    public Task<int> AppendAsync(IReadOnlyList<SecurityEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_sync)
        {
            foreach (var item in events)
                _events.AddLast(item);
            while (_events.Count > Capacity)
                _events.RemoveFirst();
        }

        return Task.FromResult(events.Count);
    }

    public Task<PagedResult<SecurityEvent>> QueryAsync(EventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalize();
        SecurityEvent[] snapshot;
        lock (_sync)
            snapshot = _events.Reverse().ToArray();

        IEnumerable<SecurityEvent> items = snapshot;
        if (!string.IsNullOrWhiteSpace(query.Category))
            items = items.Where(e => string.Equals(e.Category, query.Category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Actor))
            items = items.Where(e => string.Equals(e.Actor, query.Actor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.DeviceId))
            items = items.Where(e => string.Equals(e.DeviceId, query.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Search))
            items = items.Where(e => e.Message.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase));
        if (query.From is { } from)
            items = items.Where(e => e.Timestamp >= from);
        if (query.To is { } to)
            items = items.Where(e => e.Timestamp <= to);
        return Task.FromResult(PagedResult<SecurityEvent>.From(items, query.Page, query.PageSize));
    }

    public Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var removed = 0;
        lock (_sync)
        {
            while (_events.First is { } first && first.Value.Timestamp < olderThan)
            {
                _events.RemoveFirst();
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
