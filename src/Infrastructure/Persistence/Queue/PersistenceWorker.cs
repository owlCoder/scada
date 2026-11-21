using Application.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Queue;

/// <summary>
/// Drains the write-behind queue in batches. Each failing kind is retried with linear backoff up to
/// <see cref="PersistenceQueueOptions.MaxAttempts"/> times and then counted as failed so the queue keeps moving.
/// </summary>
public sealed class PersistenceWorker(
    ChannelPersistenceQueue queue,
    PersistenceDispatcher dispatcher,
    PersistenceQueueOptions options,
    TimeProvider time,
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
            using var drain = new CancellationTokenSource(ShutdownDrainTimeout, time);
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

            await Task.WhenAll(batch.GroupBy(item => item.GetType())
                .Select(group => PersistAsync(group.ToArray(), cancellationToken)));
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
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= options.MaxAttempts)
                {
                    logger.LogError(exception, "Persisting {Count} {Kind} item(s) failed after {Attempts} attempts",
                        batch.Count, batch[0].Kind, attempt);
                    queue.MarkFailed(batch.Count, exception.Message);
                    return false;
                }

                logger.LogWarning(exception, "Persistence attempt {Attempt} for {Kind} failed; retrying", attempt, batch[0].Kind);
                await Task.Delay(options.RetryDelay * attempt, time, cancellationToken);
            }
        }
    }
}
