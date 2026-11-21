namespace Domain.Grid.Definition;

/// <summary>
/// A switching device between a bus and the terminal of one element (line, transformer, load, generator or capacitor)
/// identified by <see cref="ElementId"/>. Opening it disconnects that element from <see cref="BusId"/>.
/// </summary>
public sealed record GridSwitchDefinition(
    string Id,
    string Name,
    GridSwitchKind Kind,
    string BusId,
    string ElementId,
    bool NormallyClosed,
    double? BreakingCapacityKa,
    DiagramPosition Position);
