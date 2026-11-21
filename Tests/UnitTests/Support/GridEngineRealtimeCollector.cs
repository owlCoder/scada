using Application.Realtime;

namespace UnitTests.Support;

/// <summary>Records every realtime publish so tests can assert topics and payloads.</summary>
public sealed class GridEngineRealtimeCollector : IRealtimePublisher
{
    public List<(string Topic, object Payload)> Messages { get; } = [];
    public bool Publish(string topic, object payload)
    {
        lock (Messages) Messages.Add((topic, payload));
        return true;
    }

    public IReadOnlyList<(string Topic, object Payload)> Snapshot()
    {
        lock (Messages) return Messages.ToArray();
    }
}
