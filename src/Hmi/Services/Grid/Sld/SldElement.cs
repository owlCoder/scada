using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// One static diagram element. All coordinates are SVG units (one grid unit = <see cref="GridDiagramGeometry.Unit"/>).
/// <see cref="Anchor"/> is where the symbol and labels sit; <see cref="Wires"/> are the polylines the element owns
/// (a line owns its full path, a transformer two stubs, a stub element one bus-to-element path, a bus none).
/// </summary>
public sealed record SldElement(
    string Id,
    string Name,
    SldElementKind Kind,
    DiagramPosition Anchor,
    double VoltageKv,
    IReadOnlyList<IReadOnlyList<DiagramPosition>> Wires,
    GridSwitchKind? SwitchKind,
    string? BusId,
    string? ElementId);
