using Application.Simulation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Simulation.Configuration;

namespace Simulation.HostedServices;

/// <summary>Real-time loop: one step every StepSeconds / TimeScale wall-clock seconds. Paused steps are skipped by the engine.</summary>
public sealed class SimulationService(
    SimulationCoordinator coordinator,
    SimulationEngine engine,
    SimulationHostOptions options,
    ILogger<SimulationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.RunLoop)
            return;

        var period = TimeSpan.FromSeconds(engine.Options.StepSeconds / engine.Options.TimeScale);
        using var timer = new PeriodicTimer(period);
        logger.LogInformation("Simulation loop started: {Step}s simulated every {Period} ms", engine.Options.StepSeconds,
            period.TotalMilliseconds);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await coordinator.StepAsync(false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Simulation step failed");
            }
        }
    }
}
