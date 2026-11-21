using Application.Detection;
using Application.Persistence;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryDetectionStore(int capacity = 20_000) : IDetectionStore, IRetentionTarget
{
    private readonly Lock _sync = new();
    private readonly LinkedList<DetectionRecord> _detections = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public string Name => "detections";

    public Task<bool> AppendAsync(DetectionRecord detection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detection);
        lock (_sync)
        {
            _detections.AddLast(detection);
            while (_detections.Count > Capacity)
                _detections.RemoveFirst();
        }

        return Task.FromResult(true);
    }

    /// <summary>Newest first.</summary>
    public Task<IReadOnlyList<DetectionRecord>> QueryAsync(DetectionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_sync)
            return Task.FromResult<IReadOnlyList<DetectionRecord>>(_detections.Reverse()
                .Where(d => (query.RuleId is null || d.RuleId == query.RuleId)
                    && (query.ScenarioRunId is null || d.ScenarioRunId == query.ScenarioRunId)
                    && (query.Since is null || d.Timestamp >= query.Since))
                .Take(Math.Clamp(query.Limit, 1, 5000))
                .ToArray());
    }

    public Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var removed = 0;
        lock (_sync)
        {
            while (_detections.First is { } first && first.Value.Timestamp < olderThan)
            {
                _detections.RemoveFirst();
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
