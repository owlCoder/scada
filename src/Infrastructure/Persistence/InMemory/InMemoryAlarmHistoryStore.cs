using Application.Alarms;
using Application.Persistence;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryAlarmHistoryStore(int capacity = 20_000) : IAlarmHistoryStore, IRetentionTarget
{
    private readonly Lock _sync = new();
    private readonly LinkedList<AlarmHistoryRecord> _records = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public string Name => "alarm-history";

    public Task<int> AppendAsync(IReadOnlyList<AlarmHistoryRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_sync)
        {
            foreach (var record in records)
                _records.AddLast(record);
            while (_records.Count > Capacity)
                _records.RemoveFirst();
        }

        return Task.FromResult(records.Count);
    }

    /// <summary>Newest first.</summary>
    public Task<IReadOnlyList<AlarmHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<AlarmHistoryRecord>>(_records.Reverse().Take(Math.Max(1, count)).ToArray());
    }

    public Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var removed = 0;
        lock (_sync)
        {
            while (_records.First is { } first && first.Value.Timestamp < olderThan)
            {
                _records.RemoveFirst();
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
