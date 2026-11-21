namespace Domain.Grid.Definition;

/// <summary>Upstream grid infeed (slack bus). <see cref="ShortCircuitPowerMva"/> sets the source impedance.</summary>
public sealed record GridSourceDefinition(
    string Id,
    string Name,
    string BusId,
    double VoltageSetpointPu,
    double ShortCircuitPowerMva,
    DiagramPosition Position);
