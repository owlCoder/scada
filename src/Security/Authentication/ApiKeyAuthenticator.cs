using System.Security.Cryptography;
using System.Text;
using Application.Events;
using Application.Identity;

namespace Security.Authentication;

/// <summary>
/// Authenticates X-Api-Key callers. Named accounts are resolved from the account store on every request, so disabling an
/// account, changing its role or rotating/revoking its key takes effect immediately. Shared legacy role keys remain
/// supported. Failed attempts are audited (Authentication events) and throttled per source.
/// </summary>
public sealed class ApiKeyAuthenticator(
    AccessPolicy legacy,
    IUserAccountStore accounts,
    AuthenticationThrottle throttle,
    IEventPublisher events,
    TimeProvider time)
{
    public async Task<AuthenticationResult> AuthenticateAsync(string? key, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new AuthenticationResult(AuthenticationStatus.Missing, null);
        if (throttle.IsBlocked(source))
            return new AuthenticationResult(AuthenticationStatus.Throttled, null);

        var fingerprint = ApiKeyValidator.Fingerprint(key);
        foreach (var account in await accounts.ListAsync(cancellationToken))
        {
            if (account.ApiKeyHash is null || !FixedTimeEquals(account.ApiKeyHash, fingerprint))
                continue;

            if (!account.IsEnabled || account.LockedUntil > time.GetUtcNow())
                return Fail(AuthenticationStatus.Disabled, source, $"API key of disabled or locked account {account.Username} used");
            return new AuthenticationResult(AuthenticationStatus.Succeeded, new AuthenticatedUser(account.Username, account.Role, fingerprint));
        }

        return legacy.Authenticate(key) is { } role
            ? new AuthenticationResult(AuthenticationStatus.Succeeded, new AuthenticatedUser($"legacy-{role}", role, fingerprint))
            : Fail(AuthenticationStatus.Invalid, source, "Invalid API key");
    }

    private AuthenticationResult Fail(AuthenticationStatus status, string source, string message)
    {
        var failures = throttle.RecordFailure(source);
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Authentication, EventSeverity.Medium, "api",
            $"{message} ({failures} recent failure(s))", source));
        return new AuthenticationResult(status, null);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
