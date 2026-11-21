using Application.Grid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Simulation.HostedServices;

/// <summary>Loads the active grid before stepping; electrical time advances independently of the water plant.</summary>
public sealed class GridSimulationService(GridCoordinator coordinator, GridEngine engine, IGridDefinitionStore store,
    GridOptions options, TimeProvider time, ILogger<GridSimulationService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var active = await store.GetActiveAsync(cancellationToken);
        if (active is not null) engine.Load(active.Definition, active.Version);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.StepSeconds / options.TimeScale), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await coordinator.StepAsync(false, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Grid simulation step failed"); }
        }
    }
}
