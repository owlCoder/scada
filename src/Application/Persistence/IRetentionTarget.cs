namespace Application.Persistence;

public interface IRetentionTarget
{
    string Name { get; }
    Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}
