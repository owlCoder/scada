namespace Application.Persistence;

/// <summary>Reports which storage backend is configured and whether it can be reached (used by readiness checks).</summary>
public interface IStorageProbe
{
    string Provider { get; }
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
