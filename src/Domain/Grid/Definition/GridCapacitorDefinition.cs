namespace Domain.Grid.Definition;

public sealed record GridCapacitorDefinition(
    string Id,
    string Name,
    string BusId,
    double RatedPowerMvar,
    int Steps,
    int InitialSteps,
    DiagramPosition Position);
