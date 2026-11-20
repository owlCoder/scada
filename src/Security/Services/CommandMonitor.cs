using Application;

namespace Security;

public sealed class CommandMonitor(IEventStore events)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _history = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int Limit = 10;

    public bool Check(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return false;

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _history.Keys.ToArray())
            {
                var history = _history[key];
                while (history.Count > 0 && now - history.Peek() >= Window)
                    history.Dequeue();

                if (history.Count == 0)
                    _history.Remove(key);
            }

            if (!_history.TryGetValue(identity, out var requests))
                _history[identity] = requests = new Queue<DateTimeOffset>();

            if (requests.Count >= Limit)
            {
                events.Append(new(now, "SecurityAlert", "Too many commands in a short period", identity));
                return false;
            }

            requests.Enqueue(now);
            return true;
        }
    }
}
