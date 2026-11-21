namespace Domain.Control;

/// <summary>
/// Remote terminal unit: acquires and reports field data on its scan cycle and relays supervisory commands,
/// but runs no autonomous control logic.
/// </summary>
public sealed class VirtualRtu(ControllerDefinition definition) : VirtualController(definition)
{
    protected override IReadOnlyList<ActuatorCommand> ComputeOutputs(FieldReadResult inputs) => [];
}
