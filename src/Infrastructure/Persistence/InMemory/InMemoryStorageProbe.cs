using Application.Persistence;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryStorageProbe : IStorageProbe
{
    public string Provider => "memory";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
