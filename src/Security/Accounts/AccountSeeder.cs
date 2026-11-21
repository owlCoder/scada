using Application.Identity;
using Security.Authentication;

namespace Security.Accounts;

/// <summary>
/// Creates configured accounts that do not exist yet. Configuration only bootstraps accounts; once an account exists it is
/// managed through <see cref="UserAdministrationService"/>. A user without a configured password gets a random one that is
/// never stored in clear text or logged, so it can only sign in with its API key until an administrator rotates the password.
/// </summary>
public sealed class AccountSeeder(IReadOnlyList<UserCredential> users, IUserAccountStore store, TimeProvider time)
{
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var user in users)
        {
            if (await store.FindAsync(user.Username, cancellationToken) is not null)
                continue;

            var now = time.GetUtcNow();
            var passwordHash = !string.IsNullOrWhiteSpace(user.PasswordHash) ? user.PasswordHash
                : PasswordHasher.Hash(string.IsNullOrEmpty(user.Password) ? PasswordHasher.GeneratePassword() : user.Password);
            if (await store.TryCreateAsync(new UserAccount(user.Username, user.Username, user.Role, passwordHash,
                user.HasApiKey ? ApiKeyValidator.Fingerprint(user.ApiKey) : null, true, now,
                user.HasPassword ? now : null, null, 0, null), cancellationToken))
                created++;
        }

        return created;
    }
}
