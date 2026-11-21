using Application.Identity;
using Security.Authentication;

namespace Security.Accounts;

/// <summary>
/// Creates accounts for configured API key users that do not exist yet. Configuration only bootstraps accounts; once an
/// account exists it is managed through <see cref="UserAdministrationService"/>. Generated passwords are never logged.
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

            await store.UpsertAsync(new UserAccount(user.Username, user.Username, user.Role,
                PasswordHasher.Hash(PasswordHasher.GeneratePassword()), ApiKeyValidator.Fingerprint(user.ApiKey), true,
                time.GetUtcNow(), null, null, 0, null), cancellationToken);
            created++;
        }

        return created;
    }
}
