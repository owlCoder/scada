using Application.Commands;
using Application.Events;

namespace Security.Commands;

/// <summary>Sliding-window command limiter per identity that raises one security alert per window when exceeded.</summary>
public sealed class CommandMonitor(IEventPublisher events, TimeProvider time, CommandRateOptions options) : ICommandRateLimiter
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastAlert = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    public bool TryAcquire(string identity) => Check(identity);

    public bool Check(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return false;

        lock (_sync)
        {
            var now = time.GetUtcNow();
            foreach (var key in _history.Keys.ToArray())
            {
                var history = _history[key];
                while (history.Count > 0 && now - history.Peek() >= options.Window)
                    history.Dequeue();

                if (history.Count == 0)
                {
                    _history.Remove(key);
                    _lastAlert.Remove(key);
                }
            }

            if (!_history.TryGetValue(identity, out var requests))
                _history[identity] = requests = new Queue<DateTimeOffset>();

            if (requests.Count >= options.Limit)
            {
                if (!_lastAlert.TryGetValue(identity, out var lastAlert) || now - lastAlert >= options.Window)
                {
                    _lastAlert[identity] = now;
                    events.Publish(SecurityEvent.Create(now, EventCategories.SecurityAlert, EventSeverity.Medium,
                        "command-monitor", "Too many commands in a short period", identity));
                }

                return false;
            }

            requests.Enqueue(now);
            return true;
        }
    }
}
