using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Protocols.Iec104;

/// <summary>
/// Reads the current value of a mapped point from a grid snapshot. The slave only ever reports the operator view:
/// measurements use the reported value and quality, and switch/tap states come from the reported RTU measurements —
/// never <see cref="GridMeasurementSnapshot.TrueValue"/>, spoofing state or injected faults. Pass the definition to
/// have the snapshot projected through <see cref="GridViewRedactor.ForOperators"/> first; callers that already hold
/// the redacted view (monitor/GI iterate many points over one snapshot) pass it with a null definition.
/// </summary>
public static class Iec104PointEvaluator
{
    public static Iec104Sample? Evaluate(Iec104Point point, GridSnapshot snapshot, GridDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(snapshot);
        var view = definition is null ? snapshot : GridViewRedactor.ForOperators(snapshot, definition);
        return point.Kind switch
        {
            Iec104PointKind.Measurement => EvaluateMeasurement(point, view),
            Iec104PointKind.SwitchStatus => EvaluateSwitch(point, view),
            Iec104PointKind.TapPosition => EvaluateTap(point, view),
            Iec104PointKind.ProtectionTrip => EvaluateProtection(point, view),
            Iec104PointKind.RtuStatus => EvaluateRtuStatus(point, view),
            _ => null
        };
    }

    private static Iec104Sample? EvaluateMeasurement(Iec104Point point, GridSnapshot snapshot)
    {
        if (point.MeasurementKind is not { } kind)
            return null;
        var matches = snapshot.Measurements
            .Where(m => m.RtuId == point.RtuId && m.ElementId == point.ElementId && m.Kind == kind)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        if (point.MeasurementIndex >= matches.Count)
            return null;
        var measurement = matches[point.MeasurementIndex];
        return new Iec104Sample(point.Kind, measurement.ReportedValue, null, null, null, false,
            Iec104ElementCodec.QualityByte(measurement.Quality));
    }

    private static Iec104Sample? EvaluateSwitch(Iec104Point point, GridSnapshot snapshot)
    {
        var switchSnapshot = snapshot.Switch(point.ElementId);
        if (switchSnapshot is null)
            return null;
        return new Iec104Sample(point.Kind, null, null,
            switchSnapshot.Closed ? Iec104ElementCodec.DoublePointClosed : Iec104ElementCodec.DoublePointOpen, null, false, 0);
    }

    private static Iec104Sample? EvaluateTap(Iec104Point point, GridSnapshot snapshot)
    {
        var branch = snapshot.Branch(point.ElementId);
        if (branch?.TapPosition is not { } tap)
            return null;
        return new Iec104Sample(point.Kind, null, null, null, tap, false, 0);
    }

    private static Iec104Sample? EvaluateProtection(Iec104Point point, GridSnapshot snapshot) =>
        new(point.Kind, null, snapshot.Protections.FirstOrDefault(p => p.Id == point.ElementId)?.Tripped == true ? 1 : 0,
            null, null, false, 0);

    private static Iec104Sample? EvaluateRtuStatus(Iec104Point point, GridSnapshot snapshot) =>
        new(point.Kind, null,
            snapshot.Alarms.Any(a => a.Active && a.Type == GridAlarmType.CommunicationLoss && a.SourceId == point.RtuId) ? 0 : 1,
            null, null, false, 0);
}
