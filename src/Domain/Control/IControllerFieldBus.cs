using Domain.Network;

namespace Domain.Control;

/// <summary>Field-level I/O as seen by a controller. Implementations route every exchange through the virtual network.</summary>
public interface IControllerFieldBus
{
    FieldReadResult ReadInputs(ControllerDefinition controller);

    TransmissionOutcome WriteOutput(ControllerDefinition controller, ActuatorCommand command);
}
