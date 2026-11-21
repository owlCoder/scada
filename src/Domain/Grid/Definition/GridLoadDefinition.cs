namespace Domain.Grid.Definition;

/// <summary>Aggregated consumers at a bus. Demand follows <see cref="Profile"/> over the simulated day.</summary>
public sealed record GridLoadDefinition(
    string Id,
    string Name,
    string BusId,
    double PeakActivePowerMw,
    double PowerFactor,
    GridLoadProfile Profile,
    int Customers,
    bool Critical,
    DiagramPosition Position);
