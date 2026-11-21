using Application.Identity;

namespace Infrastructure.Persistence.InMemory;

/// <summary>Usernames are case-insensitive.</summary>
public sealed class InMemoryUserAccountStore : IUserAccountStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, UserAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);

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

    public Task<bool> UpsertAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Username))
            return Task.FromResult(false);

        lock (_sync)
            _accounts[account.Username.Trim()] = account;
        return Task.FromResult(true);
    }
}
