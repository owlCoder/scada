using Domain.Grid.Definition;

namespace Domain.Grid.Snapshots;

/// <summary><see cref="Selected"/> is the select-before-operate state; a selection expires after the configured timeout.</summary>
public sealed record SwitchSnapshot(
    string Id,
    string Name,
    GridSwitchKind Kind,
    string BusId,
    string ElementId,
    bool Closed,
    bool Selected,
    bool? SelectedToClose,
    string? LastOperatedBy,
    GridChangeCause LastCause,
    GridEquipmentFault Fault);
