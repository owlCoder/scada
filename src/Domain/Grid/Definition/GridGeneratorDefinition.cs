namespace Domain.Grid.Definition;

/// <summary>Distributed generation modelled as a PQ injection (photovoltaic output follows the simulated sun).</summary>
public sealed record GridGeneratorDefinition(
    string Id,
    string Name,
    string BusId,
    GridGeneratorKind Kind,
    double RatedPowerMw,
    DiagramPosition Position);
