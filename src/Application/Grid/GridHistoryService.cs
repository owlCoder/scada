using Domain.Grid.Definition;

namespace Application.Grid;

/// <summary>
/// In-memory grid historian behind <see cref="IGridHistory"/>. As an <see cref="IGridObserver"/> it records, on every
/// <c>Grid:HistoryEverySteps</c>-th grid step, the REPORTED measurement values of
/// <see cref="GridViewRedactor.ForOperators"/> into a bounded ring buffer (<see cref="Capacity"/> points per
/// measurement) — never true values, so a spoofed measurement trends as the spoofed (operator-visible) value. The
/// history is cleared when a different definition is activated. All reads are thread-safe.
/// </summary>
public sealed class GridHistoryService(IGridView view, GridOptions options) : IGridHistory, IGridObserver
{
    /// <summary>Ring-buffer capacity per measurement, in points.</summary>
    public const int Capacity = 2000;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<GridHistoryPoint>> _series = new(StringComparer.Ordinal);
    private GridDefinition? _definition;
    private long _lastRecordedStep = -1;

    public IReadOnlyList<string> MeasurementIds
    {
        get { lock (_sync) { SyncDefinition(); return [.. _series.Keys]; } }
    }

    public IReadOnlyList<GridHistoryPoint> Series(string measurementId, int maxPoints)
    {
        lock (_sync)
        {
            SyncDefinition();
            if (maxPoints <= 0 || !_series.TryGetValue(measurementId, out var queue)) return [];
            var points = queue.ToArray();
            var take = Math.Min(maxPoints, points.Length);
            return points[^take..];
        }
    }

    public ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = report.Snapshot;
        var every = Math.Max(1, options.HistoryEverySteps);
        if (snapshot.StepCount % every != 0) return new ValueTask<int>(0);
        // Redact first: only reported values and their quality ever enter the historian.
        var redacted = GridViewRedactor.ForOperators(snapshot, view.Definition);
        lock (_sync)
        {
            SyncDefinition();
            if (snapshot.StepCount <= _lastRecordedStep) return new ValueTask<int>(0);
            _lastRecordedStep = snapshot.StepCount;
            var recorded = 0;
            foreach (var measurement in redacted.Measurements)
            {
                if (!_series.TryGetValue(measurement.Id, out var queue))
                {
                    queue = new Queue<GridHistoryPoint>();
                    _series[measurement.Id] = queue;
                }
                queue.Enqueue(new GridHistoryPoint(snapshot.SimulationTime, report.Timestamp, measurement.ReportedValue, measurement.Quality));
                while (queue.Count > Capacity) queue.Dequeue();
                recorded++;
            }
            return new ValueTask<int>(recorded);
        }
    }

    /// <summary>Must be called under <see cref="_sync"/>: clears everything when the active definition changed and
    /// returns true when samples were dropped.</summary>
    private bool SyncDefinition()
    {
        var current = view.Definition;
        if (ReferenceEquals(_definition, current)) return false;
        var hadSamples = _series.Count > 0;
        _definition = current;
        _series.Clear();
        _lastRecordedStep = -1;
        return hadSamples;
    }
}
