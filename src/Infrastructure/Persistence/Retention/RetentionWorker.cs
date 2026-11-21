using Application.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Retention;

/// <summary>Periodically purges data older than the configured age from every <see cref="IRetentionTarget"/>.</summary>
public sealed class RetentionWorker(
    IEnumerable<IRetentionTarget> targets,
    RetentionOptions options,
    TimeProvider timeProvider,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private readonly IRetentionTarget[] _targets = targets.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        using var timer = new PeriodicTimer(options.Interval, timeProvider);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Retention worker stopped");
        }
    }

    /// <summary>Purges every target once and returns the number of removed records per target.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var removed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var target in _targets)
        {
            try
            {
                removed[target.Name] = await target.PurgeAsync(now - options.MaxAgeFor(target.Name), cancellationToken);
                if (removed[target.Name] > 0)
                    logger.LogInformation("Retention removed {Count} record(s) from {Target}", removed[target.Name], target.Name);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One failing target must not stop retention for the others.
                logger.LogError(exception, "Retention failed for {Target}", target.Name);
                removed[target.Name] = 0;
            }
        }

        return removed;
    }
}
