namespace Domain.Process.Configuration;

public sealed record TankDefinition(
    string Id,
    string Name,
    double CapacityCubicMeters,
    double InitialLevelPercent,
    TankThresholds Thresholds);
