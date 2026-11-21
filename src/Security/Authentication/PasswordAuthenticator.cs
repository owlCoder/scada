using Application.Events;
using Application.Identity;

namespace Security.Authentication;

/// <summary>
/// Username/password sign-in against the account store. Failed attempts are audited, throttled per source and counted
/// per account; an account is locked for <see cref="PasswordLockoutOptions.LockoutDuration"/> after too many failures.
/// Passwords are never logged or included in events.
/// </summary>
public sealed class PasswordAuthenticator(
    IUserAccountStore accounts,
    AuthenticationThrottle throttle,
    PasswordLockoutOptions lockout,
    IEventPublisher events,
    TimeProvider time)
{
    // Verifying against a real hash for unknown users keeps the response time independent of whether the user exists.
    private static readonly Lazy<string> TimingHash = new(() => PasswordHasher.Hash(PasswordHasher.GeneratePassword()));

    public static string CredentialIdFor(string username) => $"password:{username.Trim().ToLowerInvariant()}";

    public async Task<AuthenticationResult> AuthenticateAsync(string? username, string? password, string source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new AuthenticationResult(AuthenticationStatus.Missing, null);
        if (throttle.IsBlocked(source))
            return new AuthenticationResult(AuthenticationStatus.Throttled, null);

        var name = username.Trim();
        var now = time.GetUtcNow();
        if (await accounts.FindAsync(name, cancellationToken) is not { } account)
        {
            PasswordHasher.Verify(password, TimingHash.Value);
            return Fail(AuthenticationStatus.Invalid, source, $"Failed sign-in for unknown user {Truncate(name)}");
        }

        if (!account.IsEnabled)
            return Fail(AuthenticationStatus.Disabled, source, $"Sign-in attempt for disabled account {account.Username}");
        if (account.LockedUntil > now)
            return Fail(AuthenticationStatus.Locked, source, $"Sign-in attempt for locked account {account.Username}");

        if (!PasswordHasher.Verify(password, account.PasswordHash))
        {
            var failures = account.FailedLoginCount + 1;
            var locked = failures >= lockout.MaxFailedAttempts;
            await accounts.UpsertAsync(account with
            {
                FailedLoginCount = locked ? 0 : failures,
                LockedUntil = locked ? now + lockout.LockoutDuration : account.LockedUntil
            }, cancellationToken);
            return locked
                ? Fail(AuthenticationStatus.Locked, source, $"Account {account.Username} locked after {failures} failed sign-ins")
                : Fail(AuthenticationStatus.Invalid, source, $"Failed sign-in for {account.Username} ({failures} consecutive)");
        }

        await accounts.UpsertAsync(account with { FailedLoginCount = 0, LockedUntil = null, LastLoginAt = now }, cancellationToken);
        events.Publish(SecurityEvent.Create(now, EventCategories.Authentication, EventSeverity.Information, "account",
            $"{account.Username} signed in", account.Username));
        return new AuthenticationResult(AuthenticationStatus.Succeeded,
            new AuthenticatedUser(account.Username, account.Role, CredentialIdFor(account.Username)));
    }

    /// <summary>
    /// Re-checks a signed-in session against the store: the account must still exist, be enabled and unlocked, and its
    /// password must not have changed since the session was issued. Returns the user with the account's current role.
    /// </summary>
    public async Task<AuthenticatedUser?> ValidateSessionAsync(string username, DateTimeOffset issuedAt, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(username, cancellationToken) is not { } account)
            return null;
        if (!account.IsEnabled || account.LockedUntil > time.GetUtcNow())
            return null;
        // Cookie timestamps have second precision; allow for that when comparing with the password change time.
        if (account.PasswordChangedAt is { } changed && changed > issuedAt.AddSeconds(1))
            return null;
        return new AuthenticatedUser(account.Username, account.Role, CredentialIdFor(account.Username));
    }

    private AuthenticationResult Fail(AuthenticationStatus status, string source, string message)
    {
        var failures = throttle.RecordFailure(source);
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Authentication, EventSeverity.Medium, "account",
            $"{message} ({failures} recent failure(s) from this source)", source));
        return new AuthenticationResult(status, null);
    }

    private static string Truncate(string value) => value.Length <= 64 ? value : value[..64];
}
