using Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Simulation;

public sealed class IncidentMonitorService(
    IEventStore events,
    IncidentService incidents,
    ILogger<IncidentMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                incidents.Observe(events.GetRecent());
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Security event processing error");
            }
        }
    }
}
