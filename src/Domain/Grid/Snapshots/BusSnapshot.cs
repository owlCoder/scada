namespace Domain.Grid.Snapshots;

public sealed record BusSnapshot(
    string Id,
    string Name,
    double NominalVoltageKv,
    bool Energized,
    double VoltageKv,
    double VoltagePu,
    double AngleDegrees);
