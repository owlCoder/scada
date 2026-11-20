using Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Simulation;

public sealed class SimulationService(IPlantService plant, ILogger<SimulationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                plant.Advance(1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Simulation error");
            }
        }
    }
}
