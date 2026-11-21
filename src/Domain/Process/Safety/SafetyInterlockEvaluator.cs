using Domain.Process.Configuration;
using Domain.Process.Devices;

namespace Domain.Process.Safety;

/// <summary>
/// Hard-wired safety logic. It acts on physical process values (an independent safety instrumented system),
/// not on reported sensor values, so sensor spoofing cannot defeat it unless interlocks are bypassed.
/// </summary>
public sealed class SafetyInterlockEvaluator(ProcessLimits limits)
{
    public InterlockDecision CanStartPump(Pump pump, IReadOnlyDictionary<string, Tank> tanks)
    {
        var target = tanks[pump.Definition.TargetTankId];
        var source = tanks[pump.Definition.SourceTankId];
        if (target.LevelPercent >= target.Definition.Thresholds.HighHigh)
            return InterlockDecision.Block($"High-high level in {target.Id} (overfill protection)");
        if (source.LevelPercent <= source.Definition.Thresholds.LowLow)
            return InterlockDecision.Block($"Low-low level in {source.Id} (dry-run protection)");
        return InterlockDecision.Allow;
    }

    public InterlockDecision CanOpenInlet(Valve valve, IReadOnlyDictionary<string, Tank> tanks)
    {
        if (valve.Definition.Role != ValveRole.Inlet)
            return InterlockDecision.Allow;

        var tank = tanks[valve.Definition.TankId!];
        return tank.LevelPercent >= tank.Definition.Thresholds.HighHigh
            ? InterlockDecision.Block($"High-high level in {tank.Id} (overfill protection)")
            : InterlockDecision.Allow;
    }

    public IReadOnlyList<InterlockActivation> Evaluate(
        IReadOnlyList<Pump> pumps,
        IReadOnlyDictionary<string, Valve> valves,
        IReadOnlyDictionary<string, Tank> tanks)
    {
        var activations = new List<InterlockActivation>();
        foreach (var pump in pumps.Where(p => p.IsCommandedOn))
        {
            var decision = CanStartPump(pump, tanks);
            if (!decision.Permitted)
                activations.Add(new(pump.Id, decision.Reason!));
            else if (pump.DeadheadSeconds >= limits.DeadheadTripSeconds)
                activations.Add(new(pump.Id, "Discharge blocked (deadhead protection)"));
            else if (pump.DischargePressure > limits.MaxDischargePressureBar * 1.15)
                activations.Add(new(pump.Id, "Discharge pressure above trip limit"));
        }

        foreach (var valve in valves.Values.Where(v => v.Definition.Role == ValveRole.Inlet && v.Setpoint > 0))
        {
            var decision = CanOpenInlet(valve, tanks);
            if (!decision.Permitted)
                activations.Add(new(valve.Id, decision.Reason!));
        }

        return activations;
    }
}
