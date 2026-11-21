using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Domain.Process.Snapshots;

namespace Application.Telemetry;

/// <summary>
/// In-memory historian holding a bounded, hash-chained window of sensor samples. The chain lets the
/// detection engine notice historian tampering (data integrity violations).
/// </summary>
public sealed class TelemetryHistorian(int capacity = 20000)
{
    private readonly Lock _sync = new();
    private readonly LinkedList<TelemetrySample> _samples = new();
    private long _sequence;
    private string _lastHash = new('0', 64);

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public IReadOnlyList<TelemetrySample> Record(PlantSnapshot snapshot, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            var recorded = new List<TelemetrySample>(snapshot.Sensors.Count);
            foreach (var sensor in snapshot.Sensors)
            {
                var sequence = ++_sequence;
                var quality = sensor.Quality.ToString();
                var hash = ComputeHash(_lastHash, sequence, sensor.Id, sensor.ReportedValue, quality);
                var sample = new TelemetrySample(sequence, timestamp, snapshot.SimulationTime, sensor.Id,
                    sensor.ReportedValue, quality, hash);
                _lastHash = hash;
                _samples.AddLast(sample);
                recorded.Add(sample);
            }

            while (_samples.Count > Capacity)
                _samples.RemoveFirst();
            return recorded;
        }
    }

    public IReadOnlyList<TelemetryPoint> GetSeries(string tagId, int count)
    {
        lock (_sync)
            return _samples.Where(s => s.TagId == tagId)
                .TakeLast(Math.Clamp(count, 1, Capacity))
                .Select(s => new TelemetryPoint(s.Timestamp, s.SimulationTime, s.Value, s.Quality))
                .ToArray();
    }

    public IReadOnlyList<string> Tags()
    {
        lock (_sync)
            return _samples.Select(s => s.TagId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Verifies the hash chain of the newest <paramref name="window"/> samples.</summary>
    public IntegrityReport Verify(int window = 2000)
    {
        lock (_sync)
        {
            var samples = _samples.TakeLast(Math.Clamp(window, 2, Capacity)).ToArray();
            for (var i = 1; i < samples.Length; i++)
            {
                var expected = ComputeHash(samples[i - 1].Hash, samples[i].Sequence, samples[i].TagId,
                    samples[i].Value, samples[i].Quality);
                if (!string.Equals(expected, samples[i].Hash, StringComparison.Ordinal))
                    return new IntegrityReport(false, samples.Length, samples[i].Sequence, samples[i].TagId);
            }

            return new IntegrityReport(true, samples.Length, null, null);
        }
    }

    /// <summary>Training hook: silently rewrites a recent historian value without updating its hash.</summary>
    public bool Tamper(string tagId, double newValue)
    {
        lock (_sync)
        {
            var node = _samples.Last;
            var skipped = 0;
            while (node is not null && (node.Value.TagId != tagId || skipped++ < 1))
                node = node.Previous;
            if (node is null)
                return false;

            node.Value = node.Value with { Value = newValue };
            return true;
        }
    }

    private static string ComputeHash(string previous, long sequence, string tag, double value, string quality)
    {
        var payload = string.Create(CultureInfo.InvariantCulture, $"{previous}|{sequence}|{tag}|{value:R}|{quality}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
