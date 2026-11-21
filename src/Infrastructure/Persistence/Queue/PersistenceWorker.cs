using Application.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Queue;

/// <summary>
/// Drains the write-behind queue in batches. A failing batch is retried with linear backoff up to
/// <see cref="PersistenceQueueOptions.MaxAttempts"/> times and then counted as failed so the queue keeps moving.
/// </summary>
public sealed class PersistenceWorker(
    ChannelPersistenceQueue queue,
    PersistenceDispatcher dispatcher,
    PersistenceQueueOptions options,
    ILogger<PersistenceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.WaitToReadAsync(stoppingToken))
                await DrainAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            using var drain = new CancellationTokenSource(ShutdownDrainTimeout);
            try
            {
                await DrainAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Persistence queue not fully drained at shutdown ({Pending} pending)", queue.Status.Pending);
            }
        }
    }

    /// <summary>Processes everything currently queued. Exposed for tests and shutdown.</summary>
    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        var batch = new List<PersistenceWorkItem>(options.BatchSize);
        while (true)
        {
            batch.Clear();
            while (batch.Count < options.BatchSize && queue.TryDequeue(out var item) && item is not null)
                batch.Add(item);
            if (batch.Count == 0)
                return processed;

            await PersistAsync(batch, cancellationToken);
            processed += batch.Count;
        }
    }

    private async Task<bool> PersistAsync(IReadOnlyList<PersistenceWorkItem> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dispatcher.DispatchAsync(batch, cancellationToken);
                queue.MarkPersisted(batch.Count);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt >= options.MaxAttempts)
                {
                    logger.LogError(exception, "Persisting {Count} item(s) failed after {Attempts} attempts", batch.Count, attempt);
                    queue.MarkFailed(batch.Count, exception.Message);
                    return false;
                }

                logger.LogWarning(exception, "Persistence attempt {Attempt} failed; retrying", attempt);
                await Task.Delay(options.RetryDelay * attempt, cancellationToken);
            }
        }
    }
}
