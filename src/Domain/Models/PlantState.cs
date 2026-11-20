namespace Domain;

public sealed record PlantState(
    string Name,
    double TankLevel,
    double TankCapacity,
    bool PumpRunning,
    long Revision,
    bool OutletValveOpen,
    double InflowRate,
    double OutflowRate);
