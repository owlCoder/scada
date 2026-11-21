namespace Application.Simulation;

public interface IPlantStateStore
{
    Task<bool> SaveAsync(SimulationStateSnapshot state, CancellationToken cancellationToken);
    Task<SimulationStateSnapshot?> LoadLatestAsync(CancellationToken cancellationToken);
}
