using Application;

namespace Security;

public sealed class CommandMonitor(IEventStore events)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _history = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public bool Check(string actor)
    {
        lock (_sync)
        {
            if (!_history.TryGetValue(actor, out var requests))
                _history[actor] = requests = new Queue<DateTimeOffset>();

            var now = DateTimeOffset.UtcNow;
            while (requests.Count > 0 && now - requests.Peek() > TimeSpan.FromMinutes(1))
                requests.Dequeue();

            requests.Enqueue(now);
            if (requests.Count <= 10)
                return true;

            events.Append(new(now, "SecurityAlert", "Превише команди у кратком периоду", actor));
            return false;
        }
    }
}
