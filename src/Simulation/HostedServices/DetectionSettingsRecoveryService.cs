using Application.Detection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Security.Detection;

namespace Simulation.HostedServices;

/// <summary>Applies persisted detection rule settings to the engine at start-up.</summary>
public sealed class DetectionSettingsRecoveryService(
    DetectionEngine detection,
    IDetectionRuleSettingsStore store,
    ILogger<DetectionSettingsRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var applied = 0;
        foreach (var settings in await store.LoadAsync(cancellationToken))
        {
            if (detection.UpdateSettings(settings))
                applied++;
            else
                logger.LogWarning("Ignoring invalid persisted settings for rule {RuleId}", settings.RuleId);
        }

        if (applied > 0)
            logger.LogInformation("Applied persisted settings for {Count} detection rule(s)", applied);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
