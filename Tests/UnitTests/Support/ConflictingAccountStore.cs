using Application.Identity;

namespace UnitTests.Support;

public sealed class ConflictingAccountStore(IUserAccountStore store, Func<Task> beforeFirstUpdate) : IUserAccountStore
{
    public int UpdateAttempts { get; private set; }

    public Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken) =>
        store.FindAsync(username, cancellationToken);

    public Task<UserAccount?> FindByApiKeyHashAsync(string fingerprint, CancellationToken cancellationToken) =>
        store.FindByApiKeyHashAsync(fingerprint, cancellationToken);

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    public Task<bool> TryCreateAsync(UserAccount account, CancellationToken cancellationToken) =>
        store.TryCreateAsync(account, cancellationToken);

    public async Task<bool> TryUpdateAsync(UserAccount account, CancellationToken cancellationToken)
    {
        if (++UpdateAttempts == 1)
            await beforeFirstUpdate();
        return await store.TryUpdateAsync(account, cancellationToken);
    }

    public Task<UserAccount?> RecordFailedSignInAsync(string username, string passwordHash, int maxAttempts,
        DateTimeOffset lockoutUntil, DateTimeOffset now, CancellationToken cancellationToken) =>
        store.RecordFailedSignInAsync(username, passwordHash, maxAttempts, lockoutUntil, now, cancellationToken);

    public Task<UserAccount?> RecordSuccessfulSignInAsync(string username, string passwordHash, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        store.RecordSuccessfulSignInAsync(username, passwordHash, now, cancellationToken);
}
