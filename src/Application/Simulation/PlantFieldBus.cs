using Domain.Control;
using Domain.Network;
using Domain.Process;
using Domain.Process.Commands;
using Domain.Process.Devices;

namespace Application.Simulation;

/// <summary>Connects controllers to the plant through their field links on the virtual network.</summary>
public sealed class PlantFieldBus(Plant plant, VirtualNetwork network) : IControllerFieldBus
{
    private readonly List<ControllerCommandRecord> _commands = [];

    public FieldReadResult ReadInputs(ControllerDefinition controller)
    {
        var transmission = network.Transmit(controller.FieldLinkId);
        if (!transmission.Delivered)
            return FieldReadResult.Failed(transmission.Outcome);

        var measurements = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var point in controller.Inputs.Where(p => p.Signal == IoSignal.Measurement))
        {
            if (plant.FindSensor(point.DeviceId) is { } sensor)
                measurements[sensor.Id] = sensor.ReportedValue;
        }

        var actuators = new Dictionary<string, ActuatorFeedback>(StringComparer.Ordinal);
        foreach (var id in controller.ActuatorIds)
        {
            if (plant.FindPump(id) is { } pump)
                actuators[id] = new ActuatorFeedback(id, pump.IsCommandedOn ? 1 : 0, pump.Mode,
                    pump.Fault == PumpFault.None && pump.InterlockReason is null);
            else if (plant.FindValve(id) is { } valve)
                actuators[id] = new ActuatorFeedback(id, valve.Setpoint, valve.Mode, valve.Fault == ValveFault.None);
        }

        return new FieldReadResult(TransmissionOutcome.Delivered, measurements, actuators);
    }

    public TransmissionOutcome WriteOutput(ControllerDefinition controller, ActuatorCommand command)
    {
        var transmission = network.Transmit(controller.FieldLinkId);
        if (!transmission.Delivered)
            return transmission.Outcome;

        var origin = CommandOrigin.Controller(controller.Id);
        var result = plant.FindPump(command.DeviceId) is not null
            ? plant.CommandPump(command.DeviceId, command.Value >= 0.5, origin)
            : plant.CommandValve(command.DeviceId, command.Value, origin);
        _commands.Add(new ControllerCommandRecord(controller.Id, command.DeviceId, command.Value, result));
        return TransmissionOutcome.Delivered;
    }

    public IReadOnlyList<ControllerCommandRecord> Drain()
    {
        var drained = _commands.ToArray();
        _commands.Clear();
        return drained;
    }
}
