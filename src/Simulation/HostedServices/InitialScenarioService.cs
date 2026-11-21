using Application.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Security.Scenarios;
using Simulation.Configuration;

namespace Simulation.HostedServices;

/// <summary>Starts the configured initial training scenario once the host is running.</summary>
public sealed class InitialScenarioService(
    ScenarioEngine scenarios,
    SimulationHostOptions options,
    ILogger<InitialScenarioService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.InitialScenario))
            return;

        var result = await scenarios.StartAsync(options.InitialScenario, Actor.System("startup"), cancellationToken);
        if (result.Succeeded)
            logger.LogInformation("Initial scenario {Scenario} started", options.InitialScenario);
        else
            logger.LogWarning("Initial scenario {Scenario} not started: {Reason}", options.InitialScenario, result.Message);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
