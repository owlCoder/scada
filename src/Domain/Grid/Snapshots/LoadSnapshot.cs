namespace Domain.Grid.Snapshots;

public sealed record LoadSnapshot(
    string Id,
    string Name,
    string BusId,
    bool Energized,
    double DemandMw,
    double ActivePowerMw,
    double ReactivePowerMvar,
    int Customers,
    int CustomersSupplied,
    bool Shed,
    bool Critical);
