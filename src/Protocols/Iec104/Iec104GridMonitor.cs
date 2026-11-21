using Application.Events;
using Application.Grid;

namespace Protocols.Iec104;

/// <summary>
/// Forwards grid steps to all started IEC 104 connections as spontaneous (COT 3) transmissions. Changed values are
/// detected against the previous step with a deadband of 0.5 % of the measurement range; the first observed step only
/// primes the baseline. Measurement values are the reported (operator-visible) ones.
/// </summary>
public sealed class Iec104GridMonitor(
    Iec104Server server,
    Iec104AddressMap map,
    IEventPublisher events,
    TimeProvider time) : IGridObserver
{
    private readonly Lock _sync = new();
    private readonly Dictionary<(int CommonAddress, int Ioa), Iec104Sample> _last = new();
    private bool _primed;

    public ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var frames = new List<Asdu>();
        lock (_sync)
        {
            var changed = new List<(Iec104Point Point, Iec104Sample Sample)>();
            foreach (var rtu in map.Rtus)
            {
                foreach (var point in rtu.Points)
                {
                    var key = (rtu.CommonAddress, point.InformationObjectAddress);
                    var sample = Iec104PointEvaluator.Evaluate(point, report.Snapshot);
                    if (sample is null)
                    {
                        _last.Remove(key);
                        continue;
                    }

                    if (_last.TryGetValue(key, out var previous) && !sample.ExceedsDeadband(previous, point.Range))
                    {
                        _last[key] = sample;
                        continue;
                    }

                    if (_primed)
                        changed.Add((point, sample));
                    _last[key] = sample;
                }
            }

            _primed = true;
            foreach (var (point, sample) in changed)
            {
                var (type, data) = Iec104SampleEncoder.Encode(sample, withTimeTag: true, report.Timestamp);
                frames.Add(Asdu.Build(
                    new AsduHeader(type, false, 1, CauseOfTransmission.Spontaneous, 0, point.CommonAddress),
                    [new Iec104InformationObject(point.InformationObjectAddress, data)]));
            }
        }

        var sent = server.Broadcast(frames);
        if (frames.Count > 0 && sent == 0)
            Audit($"{frames.Count} changed grid points had no started IEC 104 connection to send to.", EventSeverity.Low);
        return ValueTask.FromResult(Math.Max(1, frames.Count));
    }

    private bool Audit(string message, EventSeverity severity) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Protocol, severity, "iec104", message,
            "iec104-server"));
}
