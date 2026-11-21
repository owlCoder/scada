namespace Application.Identity;

public interface IUserAccountStore
{
    Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken);
    Task<UserAccount?> FindByApiKeyHashAsync(string fingerprint, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(UserAccount account, CancellationToken cancellationToken);
    Task<bool> TryUpdateAsync(UserAccount account, CancellationToken cancellationToken);

    // Sign-in writes apply only to an enabled, unlocked account with the verified password hash.
    Task<UserAccount?> RecordFailedSignInAsync(string username, string passwordHash, int maxAttempts,
        DateTimeOffset lockoutUntil, DateTimeOffset now, CancellationToken cancellationToken);
    Task<UserAccount?> RecordSuccessfulSignInAsync(string username, string passwordHash, DateTimeOffset now,
        CancellationToken cancellationToken);
}
