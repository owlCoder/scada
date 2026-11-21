using Application.Events;
using Application.Grid;

namespace Protocols.Iec104;

/// <summary>
/// Forwards grid steps to all started IEC 104 connections as spontaneous (COT 3) transmissions. Changed values are
/// detected against the previous step with a deadband of 0.5 % of the measurement range; the first observed step only
/// primes the baseline. Measurement values are the reported (operator-visible) ones.
/// When the active grid definition changes (a new version activated via <c>IGridRuntime.ActivateVersionAsync</c>, which
/// raises <see cref="GridStepEventKind.DefinitionActivated"/>) the address map is rebuilt through
/// <see cref="Iec104AddressMapProvider.Sync"/>, the change baseline is cleared and every started connection receives an
/// end-of-initialization M_EI_NA_1 (COT 4) per common address so masters re-run their general interrogation against the
/// new map instead of operating on stale IOAs.
/// </summary>
public sealed class Iec104GridMonitor(
    Iec104Server server,
    Iec104AddressMapProvider maps,
    IGridView view,
    IEventPublisher events,
    TimeProvider time) : IGridObserver
{
    private readonly Lock _sync = new();
    private readonly Dictionary<(int CommonAddress, int Ioa), Iec104Sample> _last = new();
    private bool _primed;

    public ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var before = maps.Current;
        // Rebuild the map when the definition instance changed (version activation); identity compare per step.
        var map = maps.Sync(view.Definition);
        var rebuilt = !ReferenceEquals(before, map);
        var frames = new List<Asdu>();
        lock (_sync)
        {
            if (rebuilt)
            {
                // The IOAs may have changed: drop the baseline so no stale IOA is ever transmitted spontaneously.
                _last.Clear();
                _primed = false;
            }

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

        if (rebuilt)
        {
            // Standard-consistent re-sync: M_EI_NA_1 with COT 4 (end of initialization) tells every started master that
            // the slave re-initialised and must be re-interrogated before further commands are accepted meaningfully.
            var endOfInitialization = map.Rtus
                .Select(rtu => Asdu.Build(
                    new AsduHeader(Iec104TypeId.M_EI_NA_1, false, 1, CauseOfTransmission.EndOfInitialization, 0, rtu.CommonAddress),
                    [new Iec104InformationObject(0, [0])]))
                .ToArray();
            var eiSent = server.Broadcast(endOfInitialization);
            Audit($"IEC 104 address map rebuilt for definition '{map.DefinitionName}': sent M_EI_NA_1 (COT 4) to {eiSent} " +
                $"started connection(s) for {map.Rtus.Count} common address(es); masters must re-run general interrogation. " +
                "Spontaneous baseline was reset.", EventSeverity.High);
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
