namespace Application.Persistence;

public sealed record PersistenceQueueStatus(
    int Capacity,
    int Pending,
    long Enqueued,
    long Persisted,
    long Dropped,
    long Failed,
    DateTimeOffset? LastFailureAt,
    string? LastError);
