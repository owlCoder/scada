namespace Domain.Control;

/// <summary>
/// Remote terminal unit: acquires and reports field data on its scan cycle and relays supervisory commands. It runs no
/// control loops; it only holds loop-less outputs at their configured automatic position.
/// </summary>
public sealed class VirtualRtu(ControllerDefinition definition) : VirtualController(definition)
{
    protected override IReadOnlyList<ActuatorCommand> ComputeOutputs(FieldReadResult inputs) => HoldAutomaticPositions(inputs, []);
}
