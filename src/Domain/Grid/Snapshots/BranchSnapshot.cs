namespace Domain.Grid.Snapshots;

/// <summary>Power flows are measured at each end, positive into the branch. <see cref="CurrentA"/> is the larger end current.</summary>
public sealed record BranchSnapshot(
    string Id,
    string Name,
    GridBranchKind Kind,
    string FromBusId,
    string ToBusId,
    bool Energized,
    double CurrentA,
    double ActivePowerFromMw,
    double ReactivePowerFromMvar,
    double ActivePowerToMw,
    double ReactivePowerToMvar,
    double LossesMw,
    double LoadingPercent,
    int? TapPosition,
    bool? TapAutomatic,
    GridEquipmentFault Fault);
