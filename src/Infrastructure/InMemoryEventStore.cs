using Application;

namespace Infrastructure;

public sealed class InMemoryEventStore : IEventStore
{
    private readonly object _sync = new();
    private readonly Queue<SecurityEvent> _events = new();
    private const int Capacity = 1000;

    public void Append(SecurityEvent securityEvent)
    {
        lock (_sync)
        {
            _events.Enqueue(securityEvent);
            while (_events.Count > Capacity)
                _events.Dequeue();
        }
    }

    public IReadOnlyList<SecurityEvent> GetRecent()
    {
        lock (_sync)
            return _events.Reverse().ToArray();
    }
}
