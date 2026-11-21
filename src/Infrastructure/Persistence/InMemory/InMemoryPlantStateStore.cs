using Application.Simulation;

namespace Infrastructure.Persistence.InMemory;

/// <summary>Keeps only the latest snapshot. Survives simulation resets, not process restarts.</summary>
public sealed class InMemoryPlantStateStore : IPlantStateStore
{
    private readonly Lock _sync = new();
    private SimulationStateSnapshot? _latest;

    public Task<bool> SaveAsync(SimulationStateSnapshot state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            if (_latest is not null && _latest.SavedAt > state.SavedAt)
                return Task.FromResult(false);
            _latest = state;
        }

        return Task.FromResult(true);
    }

    public Task<SimulationStateSnapshot?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(_latest);
    }
}
