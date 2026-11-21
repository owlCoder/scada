namespace Application.Persistence;

/// <summary>
/// Write-behind queue that keeps database latency out of the simulation loop. When the queue is full
/// the item is rejected, counted as dropped and reported through health checks; it never blocks callers.
/// </summary>
public interface IPersistenceQueue
{
    bool TryEnqueue(PersistenceWorkItem item);
    PersistenceQueueStatus Status { get; }
}
