namespace Application.Events;

/// <summary>Bounded, thread-safe ring of the most recent events used by detection rules and live views.</summary>
public sealed class RecentEventBuffer(int capacity = 5000)
{
    private readonly Lock _sync = new();
    private readonly LinkedList<SecurityEvent> _events = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public int Add(SecurityEvent securityEvent)
    {
        lock (_sync)
        {
            _events.AddLast(securityEvent);
            while (_events.Count > Capacity)
                _events.RemoveFirst();
            return _events.Count;
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SecurityEvent> GetRecent(int count = 500)
    {
        lock (_sync)
            return _events.Reverse().Take(Math.Clamp(count, 1, Capacity)).ToArray();
    }

    public IReadOnlyList<SecurityEvent> Since(DateTimeOffset from)
    {
        lock (_sync)
            return _events.Where(e => e.Timestamp >= from).ToArray();
    }
}
