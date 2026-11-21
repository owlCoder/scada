namespace Domain.Control;

public static class ControllerFactory
{
    public static VirtualController Create(ControllerDefinition definition) => definition.Kind switch
    {
        ControllerKind.Plc => new VirtualPlc(definition),
        ControllerKind.Rtu => new VirtualRtu(definition),
        _ => throw new ArgumentOutOfRangeException(nameof(definition))
    };
}
