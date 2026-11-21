using Application.Simulation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Simulation.Configuration;

namespace Simulation.HostedServices;

/// <summary>
/// Restores the latest persisted simulation state before the loop starts and saves the final state on shutdown.
/// Must be registered before <see cref="SimulationService"/>.
/// </summary>
public sealed class SimulationRecoveryService(
    SimulationEngine engine,
    IPlantStateStore store,
    SimulationHostOptions options,
    ILogger<SimulationRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.RestoreOnStart)
            return;

        try
        {
            var state = await store.LoadLatestAsync(cancellationToken);
            if (state is null)
                logger.LogInformation("No persisted simulation state; starting from the configured topology");
            else if (engine.Restore(state))
                logger.LogInformation("Simulation state restored from {SavedAt} (step {Step})", state.SavedAt, state.Plant.StepCount);
            else
                logger.LogWarning("Persisted simulation state from {SavedAt} does not match the topology and was ignored", state.SavedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Simulation state could not be restored; starting fresh");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(engine.CaptureState(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Final simulation state could not be saved");
        }
    }
}
