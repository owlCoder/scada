using Application.Identity;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

/// <summary>Usernames are case-insensitive.</summary>
public sealed class PostgresUserAccountStore(IDbContextFactory<ScadaDbContext> factory) : IUserAccountStore
{
    private const int MaxAttempts = 3;

    public async Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var key = UserAccountEntity.KeyOf(username);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UsernameKey == key, cancellationToken);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.UserAccounts.AsNoTracking().OrderBy(a => a.UsernameKey).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    public async Task<bool> UpsertAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Username))
            return false;

        var key = UserAccountEntity.KeyOf(account.Username);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var row = await db.UserAccounts.FirstOrDefaultAsync(a => a.UsernameKey == key, cancellationToken);
            if (row is null)
                db.UserAccounts.Add(new UserAccountEntity().Apply(account));
            else
                row.Apply(account);

            if (await StoreWrites.SaveIgnoringDuplicateAsync(db, cancellationToken))
                return true;
        }

        return false;
    }
}
