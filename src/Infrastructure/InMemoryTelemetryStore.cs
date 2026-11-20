using Application;

namespace Infrastructure;

public sealed class InMemoryTelemetryStore : ITelemetryStore
{
    private const int Capacity = 3600;
    private readonly Queue<TelemetrySample> _samples = new();
    private readonly object _sync = new();

    public void Append(TelemetrySample sample)
    {
        lock (_sync)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > Capacity)
                _samples.Dequeue();
        }
    }

    public IReadOnlyList<TelemetrySample> GetRecent(int count)
    {
        if (count is < 1 or > Capacity)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (_sync)
            return _samples.TakeLast(count).ToArray();
    }
}
