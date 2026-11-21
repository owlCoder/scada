using Domain.Process.Devices;

namespace Domain.Control;

/// <summary>Programmable logic controller executing level-control loops each scan.</summary>
public sealed class VirtualPlc(ControllerDefinition definition) : VirtualController(definition)
{
    protected override IReadOnlyList<ActuatorCommand> ComputeOutputs(FieldReadResult inputs)
    {
        var commands = new List<ActuatorCommand>();
        for (var index = 0; index < Definition.PumpLoops.Count; index++)
            commands.AddRange(EvaluatePumpLoop(Definition.PumpLoops[index], index, inputs));
        foreach (var loop in Definition.ValveLoops)
        {
            if (EvaluateValveLoop(loop, inputs) is { } command)
                commands.Add(command);
        }

        commands.AddRange(HoldAutomaticPositions(inputs, commands));
        return commands;
    }

    private IEnumerable<ActuatorCommand> EvaluatePumpLoop(PumpControlLoop loop, int index, FieldReadResult inputs)
    {
        if (!TryReadMeasurement(loop.LevelSensorId, out var level))
            return [];

        var start = Registers.Read(RegisterArea.Holding, loop.StartLevelRegister) / 10.0;
        var stop = Registers.Read(RegisterArea.Holding, loop.StopLevelRegister) / 10.0;
        var demandRegister = (ushort)(ControllerRegisterMap.LoopDemandBase + index);
        var demand = Registers.Read(RegisterArea.Holding, demandRegister) == 1;

        if (level >= stop)
            demand = false;
        else if (level <= start)
            demand = true;

        if (loop.SourceLevelSensorId is { } source &&
            TryReadMeasurement(source, out var sourceLevel) && sourceLevel < loop.SourceMinimumLevel)
            demand = false;

        Registers.Write(RegisterArea.Holding, demandRegister, (ushort)(demand ? 1 : 0));

        var automatic = loop.PumpIds
            .Select(id => inputs.Actuators.GetValueOrDefault(id))
            .Where(f => f is not null && f.Mode == ControlMode.Automatic)
            .Cast<ActuatorFeedback>()
            .ToArray();
        var selected = demand
            ? automatic.FirstOrDefault(f => f.IsAvailable && f.Value >= 1) ?? automatic.FirstOrDefault(f => f.IsAvailable)
            : null;

        return automatic
            .Select(f => (Feedback: f, Desired: f.DeviceId == selected?.DeviceId ? 1.0 : 0.0))
            .Where(x => Math.Abs(x.Feedback.Value - x.Desired) > 0.01 && (x.Desired == 0 || x.Feedback.IsAvailable))
            .Select(x => new ActuatorCommand(x.Feedback.DeviceId, x.Desired))
            .ToArray();
    }

    private ActuatorCommand? EvaluateValveLoop(ValveControlLoop loop, FieldReadResult inputs)
    {
        if (!TryReadMeasurement(loop.LevelSensorId, out var level) ||
            !inputs.Actuators.TryGetValue(loop.ValveId, out var feedback) ||
            feedback.Mode != ControlMode.Automatic || !feedback.IsAvailable)
            return null;

        var openBelow = Registers.Read(RegisterArea.Holding, loop.OpenBelowRegister) / 10.0;
        var closeAbove = Registers.Read(RegisterArea.Holding, loop.CloseAboveRegister) / 10.0;
        double? desired = level <= openBelow ? 100 : level >= closeAbove ? 0 : null;
        return desired is { } value && Math.Abs(feedback.Value - value) > 0.01
            ? new ActuatorCommand(loop.ValveId, value)
            : null;
    }
}
