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

        return snapshot with { Sensors = sensors, Tanks = tanks };
    }
}
