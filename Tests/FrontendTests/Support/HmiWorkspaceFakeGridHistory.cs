using Application.Grid;
using Domain.Process.Devices;

namespace FrontendTests.Support;

/// <summary>Deterministic in-memory historian fake for workspace tests: series are seeded explicitly per measurement id.</summary>
public sealed class HmiWorkspaceFakeGridHistory : IGridHistory
{
    private readonly Dictionary<string, List<GridHistoryPoint>> _series = new(StringComparer.Ordinal);

    public IReadOnlyList<string> MeasurementIds => _series.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public bool Set(string measurementId, IReadOnlyList<GridHistoryPoint> points)
    {
        _series[measurementId] = [.. points];
        return true;
    }

    /// <summary>Seeds a deterministic ramp for the measurement so trends render without explicit setup.</summary>
    public bool Seed(string measurementId, double start, double step, int count)
    {
        var points = new List<GridHistoryPoint>(count);
        for (var i = 0; i < count; i++)
            points.Add(new GridHistoryPoint(TimeSpan.FromSeconds(i), DateTimeOffset.UnixEpoch, start + step * i, MeasurementQuality.Good));
        _series[measurementId] = points;
        return true;
    }

    public IReadOnlyList<GridHistoryPoint> Series(string measurementId, int maxPoints) =>
        _series.TryGetValue(measurementId, out var points) ? points.TakeLast(Math.Max(0, maxPoints)).ToArray() : [];
}
