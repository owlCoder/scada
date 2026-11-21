namespace Domain.Grid.Snapshots;

public sealed record CapacitorSnapshot(
    string Id,
    string Name,
    string BusId,
    int StepsInService,
    int Steps,
    double ReactivePowerMvar);
