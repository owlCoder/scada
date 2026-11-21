using Application.Identity;

namespace UnitTests.Support;

public sealed class SignInConflictStore(IUserAccountStore store) : IUserAccountStore
{
    public int Attempts { get; private set; }

    public Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken) => store.FindAsync(username, cancellationToken);
    public Task<UserAccount?> FindByApiKeyHashAsync(string fingerprint, CancellationToken cancellationToken) => store.FindByApiKeyHashAsync(fingerprint, cancellationToken);
    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
    public Task<bool> TryCreateAsync(UserAccount account, CancellationToken cancellationToken) => store.TryCreateAsync(account, cancellationToken);
    public Task<bool> TryUpdateAsync(UserAccount account, CancellationToken cancellationToken) => store.TryUpdateAsync(account, cancellationToken);

    public Task<UserAccount?> RecordFailedSignInAsync(string username, string passwordHash, int maxAttempts,
        DateTimeOffset lockoutUntil, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Attempts++;
        return Task.FromResult<UserAccount?>(null);
    }

    public Task<UserAccount?> RecordSuccessfulSignInAsync(string username, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Attempts++;
        return Task.FromResult<UserAccount?>(null);
    }
}
