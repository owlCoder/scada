namespace Application.Simulation;

/// <summary>Receives every simulation step after it has been applied (detection, scenarios, metrics).</summary>
public interface ISimulationObserver
{
    ValueTask<int> OnStepAsync(SimulationStepReport report, CancellationToken cancellationToken);
}
