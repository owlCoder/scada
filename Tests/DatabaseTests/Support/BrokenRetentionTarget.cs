using Application.Persistence;

namespace DatabaseTests.Support;

public sealed class BrokenRetentionTarget : IRetentionTarget
{
    public string Name => "broken";

    public Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("database offline");
}
