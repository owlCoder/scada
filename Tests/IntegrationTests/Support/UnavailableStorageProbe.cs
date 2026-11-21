using Application.Persistence;

namespace IntegrationTests.Support;

public sealed class UnavailableStorageProbe : IStorageProbe
{
    public string Provider => "postgres";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Connection refused (password=secret)");
}
