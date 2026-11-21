using Application.Identity;
using Security.Accounts;
using Security.Authentication;

namespace Api.HostedServices;

/// <summary>Creates local accounts for configured API key users that do not exist yet. Passwords are random and never logged.</summary>
public sealed class AccountSeedService(
    IReadOnlyList<UserCredential> users,
    IUserAccountStore store,
    TimeProvider time,
    ILogger<AccountSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
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

        if (created > 0)
            logger.LogInformation("Seeded {Count} local account(s) from configuration", created);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
