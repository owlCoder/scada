namespace Security.Authentication;

/// <summary>Blocks a source after too many failed authentication attempts within a sliding window.</summary>
public sealed class AuthenticationThrottle(TimeProvider time, int maxFailures = 10, TimeSpan? window = null)
{
    private const int MaxTrackedSources = 10_000;
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    public bool IsBlocked(string source)
    {
        lock (_sync)
            return Prune(source) >= maxFailures;
    }

    public int RecordFailure(string source)
    {
        lock (_sync)
        {
            if (!_failures.TryGetValue(source, out var failures))
            {
                if (_failures.Count >= MaxTrackedSources)
                    _failures.Remove(_failures.MinBy(f => f.Value.Count == 0 ? DateTimeOffset.MinValue : f.Value.Peek()).Key);
                _failures[source] = failures = new Queue<DateTimeOffset>();
            }

            failures.Enqueue(time.GetUtcNow());
            return Prune(source);
        }
    }

    private int Prune(string source)
    {
        if (!_failures.TryGetValue(source, out var failures))
            return 0;

        var now = time.GetUtcNow();
        while (failures.Count > 0 && now - failures.Peek() >= _window)
            failures.Dequeue();
        if (failures.Count == 0)
            _failures.Remove(source);
        return failures.Count;
    }
}
