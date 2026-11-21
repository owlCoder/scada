using Application.Detection;
using Application.Grid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Security.Grid.Detection;

namespace Api.Configuration.Registration;

/// <summary>
/// Applies persisted grid detection rule settings to the <see cref="GridDetectionEngine"/> at start-up. Does nothing when
/// no grid view is registered in this host (grid mode not wired up), so the water-only hosts keep working.
/// </summary>
public sealed class GridDetectionSettingsRecoveryService(
    IServiceProvider services,
    IDetectionRuleSettingsStore store,
    ILogger<GridDetectionSettingsRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IGridView>() is null)
            return;

        var detection = services.GetRequiredService<GridDetectionEngine>();
        var applied = 0;
        foreach (var settings in await store.LoadAsync(cancellationToken))
        {
            if (!settings.RuleId.StartsWith("grid-", StringComparison.Ordinal))
                continue;
            if (detection.UpdateSettings(settings))
                applied++;
            else
                logger.LogWarning("Ignoring invalid persisted settings for grid rule {RuleId}", settings.RuleId);
        }

        if (applied > 0)
            logger.LogInformation("Applied persisted settings for {Count} grid detection rule(s)", applied);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
