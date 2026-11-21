using Domain.Grid.Definition;

namespace Domain.Grid.Snapshots;

public sealed record GeneratorSnapshot(
    string Id,
    string Name,
    string BusId,
    GridGeneratorKind Kind,
    bool Connected,
    double ActivePowerMw,
    double ReactivePowerMvar);
