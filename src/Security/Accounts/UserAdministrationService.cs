using Application.Events;
using Application.Identity;
using Security.Authentication;

namespace Security.Accounts;

/// <summary>Administrator-only account management. Every denied attempt is audited as a privilege-escalation indicator.</summary>
public sealed class UserAdministrationService(IUserAccountStore store, IEventPublisher events, TimeProvider time)
{
    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return (await store.ListAsync(cancellationToken))
            .Select(a => new AccountSummary(a.Username, a.DisplayName, a.Role, a.IsEnabled, a.ApiKeyHash is not null,
                a.CreatedAt, a.PasswordChangedAt, a.LastLoginAt, a.LockedUntil > now))
            .OrderBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<AccountOperationResult> ChangeRoleAsync(string username, AccessRole role, Actor actor, CancellationToken cancellationToken) =>
        ModifyAsync(username, actor, $"change role of {username} to {role}", account =>
            Enum.IsDefined(role)
                ? (account with { Role = role }, new AccountOperationResult(AccountOperationStatus.Succeeded, $"Role changed to {role}"))
                : (null, new AccountOperationResult(AccountOperationStatus.InvalidInput, "Unknown role")), cancellationToken);

    public Task<AccountOperationResult> SetEnabledAsync(string username, bool enabled, Actor actor, CancellationToken cancellationToken) =>
        ModifyAsync(username, actor, $"{(enabled ? "enable" : "disable")} {username}", account =>
            (account with { IsEnabled = enabled, FailedLoginCount = 0, LockedUntil = null },
                new AccountOperationResult(AccountOperationStatus.Succeeded, enabled ? "Account enabled" : "Account disabled")),
            cancellationToken);

    public Task<AccountOperationResult> RotatePasswordAsync(string username, Actor actor, CancellationToken cancellationToken)
    {
        var password = PasswordHasher.GeneratePassword();
        return ModifyAsync(username, actor, $"rotate password of {username}", account =>
            (account with { PasswordHash = PasswordHasher.Hash(password), PasswordChangedAt = time.GetUtcNow(), FailedLoginCount = 0, LockedUntil = null },
                new AccountOperationResult(AccountOperationStatus.Succeeded, "Password rotated", password)), cancellationToken);
    }

    public Task<AccountOperationResult> RotateApiKeyAsync(string username, Actor actor, CancellationToken cancellationToken)
    {
        var key = PasswordHasher.GenerateApiKey();
        return ModifyAsync(username, actor, $"rotate API key of {username}", account =>
            (account with { ApiKeyHash = ApiKeyValidator.Fingerprint(key) },
                new AccountOperationResult(AccountOperationStatus.Succeeded, "API key rotated", key)), cancellationToken);
    }

    public Task<AccountOperationResult> RevokeApiKeyAsync(string username, Actor actor, CancellationToken cancellationToken) =>
        ModifyAsync(username, actor, $"revoke API key of {username}", account =>
            (account with { ApiKeyHash = null }, new AccountOperationResult(AccountOperationStatus.Succeeded, "API key revoked")),
            cancellationToken);

    private async Task<AccountOperationResult> ModifyAsync(string username, Actor actor, string description,
        Func<UserAccount, (UserAccount? Updated, AccountOperationResult Result)> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var now = time.GetUtcNow();
        if (!actor.HasRole(AccessRole.Administrator))
        {
            events.Publish(SecurityEvent.Create(now, EventCategories.AuthorizationDenied, EventSeverity.High, "accounts",
                $"Denied attempt to {description}: requires Administrator, caller is {actor.Role}", actor.Name));
            return new AccountOperationResult(AccountOperationStatus.Denied, "Administrator role required.");
        }

        if (string.IsNullOrWhiteSpace(username) || await store.FindAsync(username, cancellationToken) is not { } account)
            return new AccountOperationResult(AccountOperationStatus.NotFound, "Account not found.");

        var (updated, result) = change(account);
        if (updated is null || !result.Succeeded)
            return result;

        await store.UpsertAsync(updated, cancellationToken);
        events.Publish(SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Medium, "accounts",
            $"Account change: {description}", actor.Name));
        return result;
    }
}
