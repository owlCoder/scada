using Application.Persistence;
using Application.Telemetry;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryTelemetryStore(int capacity = 200_000) : ITelemetryStore, IRetentionTarget
{
    private const int MaxLimit = 10_000;
    private readonly Lock _sync = new();
    private readonly LinkedList<TelemetrySample> _samples = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public string Name => "telemetry";

    public Task<int> AppendAsync(IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        lock (_sync)
        {
            foreach (var sample in samples)
                _samples.AddLast(sample);
            while (_samples.Count > Capacity)
                _samples.RemoveFirst();
        }

        return Task.FromResult(samples.Count);
    }

    /// <summary>Returns the newest matching samples in chronological order.</summary>
    public Task<IReadOnlyList<TelemetrySample>> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(query), $"Limit must be between 1 and {MaxLimit}.");

        lock (_sync)
        {
            var result = _samples.Reverse()
                .Where(s => (query.TagId is null || s.TagId == query.TagId)
                    && (query.From is null || s.Timestamp >= query.From)
                    && (query.To is null || s.Timestamp <= query.To))
                .Take(query.Limit)
                .Reverse()
                .ToArray();
            return Task.FromResult<IReadOnlyList<TelemetrySample>>(result);
        }
    }

    public Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var removed = 0;
        lock (_sync)
        {
            while (_samples.First is { } first && first.Value.Timestamp < olderThan)
            {
                _samples.RemoveFirst();
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
