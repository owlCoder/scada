using Domain.Process.Configuration;
using Domain.Process.Devices;

namespace Domain.Process.Hydraulics;

/// <summary>
/// Deterministic mass-balance model. Pumps deliver nominal flow scaled by speed and discharge valve opening,
/// limited by the water available in the source tank. Gravity outlets scale with the square root of level.
/// </summary>
public static class HydraulicModel
{
    public static HydraulicResult Step(
        IReadOnlyDictionary<string, Tank> tanks,
        IReadOnlyList<Pump> pumps,
        IReadOnlyDictionary<string, Valve> valves,
        ProcessLimits limits,
        double seconds)
    {
        var flows = new Dictionary<string, double>(StringComparer.Ordinal);
        var pressures = new Dictionary<string, double>(StringComparer.Ordinal);
        var deltas = tanks.Keys.ToDictionary(id => id, _ => 0.0, StringComparer.Ordinal);
        var minutes = seconds / 60;

        foreach (var valve in valves.Values)
            valve.AdvanceTravel(seconds);
        foreach (var pump in pumps)
            pump.AdvanceRamp(seconds);

        foreach (var pump in pumps)
        {
            var valve = valves[pump.Definition.DischargeValveId];
            var source = tanks[pump.Definition.SourceTankId];
            var opening = valve.Position / 100;
            var requested = pump.Definition.NominalFlowRate * pump.Speed * opening * minutes;
            var alreadyTaken = -deltas[source.Id];
            var available = Math.Max(0, source.Volume - alreadyTaken);
            var volume = Math.Min(requested, available);
            var flowRate = minutes > 0 ? volume / minutes : 0;

            deltas[source.Id] -= volume;
            deltas[pump.Definition.TargetTankId] += volume;

            var deadheaded = pump.IsRotating && valve.Position < limits.DeadheadValvePercent;
            var pressure = pump.IsRotating
                ? pump.Definition.ShutoffPressureBar * pump.Speed * pump.Speed * (1 - 0.6 * opening)
                : 0.05 * source.LevelPercent / 100 * pump.Definition.ShutoffPressureBar;
            pump.UpdateHydraulics(flowRate, Math.Round(pressure, 4), deadheaded, seconds);
            valve.UpdateFlow(flowRate);
            flows[pump.Id] = flowRate;
            flows[valve.Id] = flowRate;
            pressures[pump.Id] = pump.DischargePressure;
        }

        foreach (var valve in valves.Values.Where(v => v.Definition.Role != ValveRole.Discharge))
        {
            var tank = tanks[valve.Definition.TankId!];
            var opening = valve.Position / 100;
            double volume;
            if (valve.Definition.Role == ValveRole.Inlet)
            {
                volume = valve.Definition.MaxFlowRate * opening * minutes;
                deltas[tank.Id] += volume;
            }
            else
            {
                var levelFactor = Math.Sqrt(Math.Max(0, tank.LevelPercent) / 100);
                var requested = valve.Definition.MaxFlowRate * opening * levelFactor * minutes;
                var available = Math.Max(0, tank.Volume + deltas[tank.Id]);
                volume = Math.Min(requested, available);
                deltas[tank.Id] -= volume;
            }

            var flowRate = minutes > 0 ? volume / minutes : 0;
            valve.UpdateFlow(flowRate);
            flows[valve.Id] = flowRate;
        }

        var overflow = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, delta) in deltas)
            overflow[id] = tanks[id].ApplyVolumeChange(delta);

        return new HydraulicResult(flows, pressures, overflow);
    }
}
