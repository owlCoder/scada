using Domain.Process.Configuration;
using Domain.Process.Devices;

namespace Domain.Process.Alarms;

/// <summary>
/// Level and pressure alarms are derived from reported sensor values, exactly as a real SCADA system sees
/// them; overflow detection uses an independent physical overflow switch.
/// </summary>
public static class AlarmConditionEvaluator
{
    public static IReadOnlyList<AlarmCondition> Evaluate(
        IReadOnlyDictionary<string, Tank> tanks,
        IReadOnlyList<Pump> pumps,
        IReadOnlyDictionary<string, Valve> valves,
        IReadOnlyList<Sensor> sensors,
        ProcessLimits limits)
    {
        var conditions = new List<AlarmCondition>();

        foreach (var sensor in sensors)
        {
            if (sensor.Quality == MeasurementQuality.Bad)
            {
                conditions.Add(new($"{sensor.Id}.QUALITY", sensor.Id, AlarmType.SensorQuality, AlarmSeverity.Medium,
                    $"{sensor.Definition.Name}: bad measurement quality"));
                continue;
            }

            if (sensor.Definition.Kind == SensorKind.Level && tanks.TryGetValue(sensor.Definition.DeviceId, out var tank))
                AddLevelAlarm(conditions, tank, sensor.ReportedValue);
            else if (sensor.Definition.Kind == SensorKind.Pressure && sensor.ReportedValue > limits.MaxDischargePressureBar)
                conditions.Add(new($"{sensor.Definition.DeviceId}.PRESSURE", sensor.Definition.DeviceId, AlarmType.HighPressure,
                    AlarmSeverity.High, $"High discharge pressure on {sensor.Definition.DeviceId}"));
        }

        foreach (var tank in tanks.Values.Where(t => t.IsOverflowing))
            conditions.Add(new($"{tank.Id}.OVERFLOW", tank.Id, AlarmType.Overflow, AlarmSeverity.Critical,
                $"{tank.Definition.Name} is overflowing"));

        foreach (var pump in pumps)
        {
            if (pump.Fault != PumpFault.None)
                conditions.Add(new($"{pump.Id}.FAULT", pump.Id, AlarmType.DeviceFault, AlarmSeverity.High,
                    $"{pump.Definition.Name} fault: {pump.Fault}"));
            if (pump.InterlockReason is not null)
                conditions.Add(new($"{pump.Id}.INTERLOCK", pump.Id, AlarmType.InterlockTrip, AlarmSeverity.High,
                    $"{pump.Definition.Name} tripped: {pump.InterlockReason}"));
        }

        foreach (var valve in valves.Values)
        {
            if (valve.Fault != ValveFault.None)
                conditions.Add(new($"{valve.Id}.FAULT", valve.Id, AlarmType.DeviceFault, AlarmSeverity.High,
                    $"{valve.Definition.Name} fault: {valve.Fault}"));
            if (valve.InterlockReason is not null)
                conditions.Add(new($"{valve.Id}.INTERLOCK", valve.Id, AlarmType.InterlockTrip, AlarmSeverity.Medium,
                    $"{valve.Definition.Name} forced closed: {valve.InterlockReason}"));
        }

        return conditions;
    }

    private static bool AddLevelAlarm(List<AlarmCondition> conditions, Tank tank, double level)
    {
        var t = tank.Definition.Thresholds;
        var name = tank.Definition.Name;
        AlarmCondition? condition =
            level >= t.HighHigh ? new($"{tank.Id}.HH", tank.Id, AlarmType.HighHigh, AlarmSeverity.Critical, $"{name} level high-high")
            : level >= t.High ? new($"{tank.Id}.H", tank.Id, AlarmType.High, AlarmSeverity.Medium, $"{name} level high")
            : level <= t.LowLow ? new($"{tank.Id}.LL", tank.Id, AlarmType.LowLow, AlarmSeverity.Critical, $"{name} level low-low")
            : level <= t.Low ? new($"{tank.Id}.L", tank.Id, AlarmType.Low, AlarmSeverity.Medium, $"{name} level low")
            : null;
        if (condition is null)
            return false;

        conditions.Add(condition);
        return true;
    }
}
