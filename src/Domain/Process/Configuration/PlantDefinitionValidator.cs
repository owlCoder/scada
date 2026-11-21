using Domain.Process.Devices;

namespace Domain.Process.Configuration;

public static class PlantDefinitionValidator
{
    public static IReadOnlyList<string> Validate(PlantDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        var tanks = definition.Tanks.Select(tank => tank.Id).ToHashSet(StringComparer.Ordinal);
        var valves = definition.Valves.ToDictionary(valve => valve.Id, StringComparer.Ordinal);
        var pumps = definition.Pumps.Select(pump => pump.Id).ToHashSet(StringComparer.Ordinal);

        var allIds = definition.Tanks.Select(x => x.Id)
            .Concat(definition.Pumps.Select(x => x.Id))
            .Concat(definition.Valves.Select(x => x.Id))
            .Concat(definition.Sensors.Select(x => x.Id))
            .ToArray();
        foreach (var duplicate in allIds.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Device identifier '{duplicate.Key}' is used more than once.");
        if (allIds.Any(string.IsNullOrWhiteSpace))
            errors.Add("Every device requires an identifier.");

        foreach (var tank in definition.Tanks)
        {
            if (tank.CapacityCubicMeters <= 0)
                errors.Add($"Tank '{tank.Id}' must have a positive capacity.");
            if (tank.InitialLevelPercent is < 0 or > 100)
                errors.Add($"Tank '{tank.Id}' initial level must be between 0 and 100 percent.");
            if (!tank.Thresholds.IsOrdered)
                errors.Add($"Tank '{tank.Id}' thresholds must satisfy LowLow < Low < High < HighHigh within 0..100.");
        }

        foreach (var pump in definition.Pumps)
        {
            if (!tanks.Contains(pump.SourceTankId) || !tanks.Contains(pump.TargetTankId))
                errors.Add($"Pump '{pump.Id}' references an unknown tank.");
            if (pump.SourceTankId == pump.TargetTankId)
                errors.Add($"Pump '{pump.Id}' cannot pump into its own source tank.");
            if (!valves.TryGetValue(pump.DischargeValveId, out var valve) || valve.Role != ValveRole.Discharge)
                errors.Add($"Pump '{pump.Id}' requires a discharge valve.");
            if (pump.NominalFlowRate <= 0 || pump.ShutoffPressureBar <= 0 || pump.RampSeconds <= 0)
                errors.Add($"Pump '{pump.Id}' requires positive flow, pressure and ramp values.");
        }

        foreach (var valve in definition.Valves)
        {
            if (valve.Role != ValveRole.Discharge && (valve.TankId is null || !tanks.Contains(valve.TankId)))
                errors.Add($"Valve '{valve.Id}' references an unknown tank.");
            if (valve.MaxFlowRate < 0 || valve.TravelPercentPerSecond <= 0)
                errors.Add($"Valve '{valve.Id}' requires a non-negative flow and positive travel rate.");
            if (valve.FailSafePosition is < 0 or > 100 || valve.InitialPosition is < 0 or > 100)
                errors.Add($"Valve '{valve.Id}' positions must be between 0 and 100 percent.");
        }

        foreach (var sensor in definition.Sensors)
        {
            var valid = sensor.Kind switch
            {
                SensorKind.Level => tanks.Contains(sensor.DeviceId),
                SensorKind.Flow => pumps.Contains(sensor.DeviceId) || valves.ContainsKey(sensor.DeviceId),
                SensorKind.Pressure => pumps.Contains(sensor.DeviceId),
                _ => false
            };
            if (!valid)
                errors.Add($"Sensor '{sensor.Id}' references an incompatible device '{sensor.DeviceId}'.");
            if (sensor.RangeMax <= sensor.RangeMin)
                errors.Add($"Sensor '{sensor.Id}' requires a valid measurement range.");
        }

        if (definition.Limits.MaxDischargePressureBar <= 0 || definition.Limits.DeadheadTripSeconds <= 0)
            errors.Add("Process limits must be positive.");

        return errors;
    }
}
