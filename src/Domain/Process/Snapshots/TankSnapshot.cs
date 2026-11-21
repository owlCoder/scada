using Domain.Process.Configuration;

namespace Domain.Process.Snapshots;

public sealed record TankSnapshot(
    string Id,
    string Name,
    double Capacity,
    double Volume,
    double LevelPercent,
    TankThresholds Thresholds,
    bool IsOverflowing,
    double TotalOverflowVolume);
