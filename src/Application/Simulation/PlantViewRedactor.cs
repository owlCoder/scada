using Domain.Process.Commands;
using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Application.Simulation;

/// <summary>
/// Produces the operator's view of the plant: only what instruments report. Ground truth (true values, spoofing,
/// injected faults, true tank contents) is reserved for instructors, otherwise trainees could see through attacks.
/// </summary>
public static class PlantViewRedactor
{
    public static PlantSnapshot ForOperators(PlantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sensors = snapshot.Sensors.Select(s => s with
        {
            TrueValue = s.ReportedValue,
            Fault = SensorFault.None,
            DriftOffset = 0,
            IsSpoofed = false,
            SpoofedValue = null,
            Health = s.Quality == MeasurementQuality.Good ? DeviceHealth.Healthy : DeviceHealth.Warning
        }).ToArray();

        var tanks = snapshot.Tanks.Select(t =>
        {
            var level = sensors.FirstOrDefault(s => s.Kind == SensorKind.Level && s.DeviceId == t.Id);
            return level is null
                ? t
                : t with { LevelPercent = level.ReportedValue, Volume = t.Capacity * level.ReportedValue / 100 };
        }).ToArray();

        var pumps = snapshot.Pumps.Select(p =>
        {
            var reason = ObservableInterlockReason(p.InterlockReason, p.Fault != PumpFault.None);
            return p with
            {
                Fault = PumpFault.None,
                InterlockReason = reason,
                Health = reason is not null ? DeviceHealth.Warning : DeviceHealth.Healthy,
                LastChangedBy = p.LastChangeCause == ChangeCause.Unattributed ? null : p.LastChangedBy
            };
        }).ToArray();
        var valves = snapshot.Valves.Select(v =>
        {
            var reason = ObservableInterlockReason(v.InterlockReason, v.Fault != ValveFault.None);
            return v with
            {
                Fault = ValveFault.None,
                InterlockReason = reason,
                Health = reason is not null ? DeviceHealth.Warning : DeviceHealth.Healthy,
                LastChangedBy = v.LastChangeCause == ChangeCause.Unattributed ? null : v.LastChangedBy
            };
        }).ToArray();

        return snapshot with { Sensors = sensors, Tanks = tanks, Pumps = pumps, Valves = valves, InterlocksBypassed = false };
    }

    private static string? ObservableInterlockReason(string? reason, bool hasHiddenFault)
    {
        // SafetyInterlockEvaluator's independent SIS trips remain observable, including when a fault
        // is also injected. Other fault-specific diagnostics must not reveal the hidden diagnosis.
        if (!hasHiddenFault || reason is null)
            return reason;
        return (reason.StartsWith("High-high level in ", StringComparison.Ordinal)
                && reason.EndsWith(" (overfill protection)", StringComparison.Ordinal))
            || (reason.StartsWith("Low-low level in ", StringComparison.Ordinal)
                && reason.EndsWith(" (dry-run protection)", StringComparison.Ordinal))
            || reason is "Discharge blocked (deadhead protection)" or "Discharge pressure above trip limit"
            ? reason : null;
    }
}
