using Application;

namespace Infrastructure;

public sealed class InMemoryEventStore : IEventStore
{
    private readonly object _sync = new();
    private readonly Queue<SecurityEvent> _events = new();
    private const int Capacity = 1000;

    public bool Append(SecurityEvent securityEvent)
    {
        lock (_sync)
        {
            _events.Enqueue(securityEvent);
            while (_events.Count > Capacity)
                _events.Dequeue();
            return true;
        }
    }

    public IReadOnlyList<SecurityEvent> GetRecent()
    {
        lock (_sync)
            return _events.Reverse().ToArray();
    }
}
