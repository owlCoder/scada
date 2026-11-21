using Application.Identity;

namespace Infrastructure.Persistence.InMemory;

/// <summary>Usernames are case-insensitive.</summary>
public sealed class InMemoryUserAccountStore : IUserAccountStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, UserAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortedSet<string>> _apiKeys = new(StringComparer.Ordinal);

    public Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(string.IsNullOrWhiteSpace(username) ? null : _accounts.GetValueOrDefault(username.Trim()));
    }

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<UserAccount>>(_accounts.Values.ToArray());
    }

    public Task<UserAccount?> FindByApiKeyHashAsync(string fingerprint, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(_apiKeys.GetValueOrDefault(fingerprint)?.Min is { } username ? _accounts[username] : null);
    }

    public Task<bool> TryCreateAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Username))
            return Task.FromResult(false);

        lock (_sync)
        {
            var username = account.Username.Trim();
            if (_accounts.ContainsKey(username))
                return Task.FromResult(false);

            _accounts.Add(username, account with { Version = 0 });
            SetApiKey(username, null, account.ApiKeyHash);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (_sync)
        {
            var key = account.Username.Trim();
            if (!_accounts.TryGetValue(key, out var current) || current.Version != account.Version)
                return Task.FromResult(false);

            _accounts[key] = account with { Version = account.Version + 1 };
            SetApiKey(key, current.ApiKeyHash, account.ApiKeyHash);
            return Task.FromResult(true);
        }
    }

    public Task<UserAccount?> RecordFailedSignInAsync(string username, string passwordHash, int maxAttempts,
        DateTimeOffset lockoutUntil, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateSignInAsync(username, passwordHash, now, account => account.FailedLoginCount >= maxAttempts - 1
            ? account with { FailedLoginCount = 0, LockedUntil = lockoutUntil }
            : account with { FailedLoginCount = account.FailedLoginCount + 1 });

    public Task<UserAccount?> RecordSuccessfulSignInAsync(string username, string passwordHash, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        UpdateSignInAsync(username, passwordHash, now, account => account with
        {
            FailedLoginCount = 0,
            LockedUntil = null,
            LastLoginAt = account.LastLoginAt > now ? account.LastLoginAt : now
        });

    private Task<UserAccount?> UpdateSignInAsync(string username, string passwordHash, DateTimeOffset now,
        Func<UserAccount, UserAccount> update)
    {
        lock (_sync)
        {
            var key = username.Trim();
            if (!_accounts.TryGetValue(key, out var account) || !account.IsEnabled
                || account.LockedUntil > now || account.PasswordHash != passwordHash)
                return Task.FromResult<UserAccount?>(null);

            var updated = update(account) with { Version = account.Version + 1 };
            _accounts[key] = updated;
            return Task.FromResult<UserAccount?>(updated);
        }
    }

    private bool SetApiKey(string username, string? previous, string? updated)
    {
        if (previous == updated)
            return false;
        if (previous is not null && _apiKeys.TryGetValue(previous, out var previousUsers))
        {
            previousUsers.Remove(username);
            if (previousUsers.Count == 0)
                _apiKeys.Remove(previous);
        }

        if (updated is not null)
        {
            if (!_apiKeys.TryGetValue(updated, out var users))
                _apiKeys.Add(updated, users = new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
            users.Add(username);
        }

        return true;
    }
}
