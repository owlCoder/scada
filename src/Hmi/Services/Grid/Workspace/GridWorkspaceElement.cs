using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Workspace;

/// <summary>One resolvable grid element of the workspace: identity, display kind, nominal voltage for colouring and the
/// matching definition record (exactly one of the definition references is set, or none for unknown elements).</summary>
public sealed record GridWorkspaceElement(
    string Id,
    string Name,
    GridWorkspaceElementKind Kind,
    double? NominalVoltageKv,
    GridBusDefinition? Bus = null,
    GridLineDefinition? Line = null,
    GridTransformerDefinition? Transformer = null,
    GridSwitchDefinition? Switch = null,
    GridLoadDefinition? Load = null,
    GridCapacitorDefinition? Capacitor = null);
