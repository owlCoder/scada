using Domain.Process.Snapshots;

namespace Application.Simulation;

/// <summary>Everything needed to resume the simulation after a restart.</summary>
public sealed record SimulationStateSnapshot(
    DateTimeOffset SavedAt,
    PlantSnapshot Plant,
    IReadOnlyList<ControllerStateRecord> Controllers,
    bool IsPaused);
