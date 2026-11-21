using Application.Identity;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

/// <summary>Usernames are case-insensitive.</summary>
public sealed class PostgresUserAccountStore(IDbContextFactory<ScadaDbContext> factory) : IUserAccountStore
{
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

    public async Task<UserAccount?> FindByApiKeyHashAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.UserAccounts.AsNoTracking().Where(a => a.ApiKeyHash == fingerprint)
            .OrderBy(a => a.UsernameKey).FirstOrDefaultAsync(cancellationToken);
        return row?.ToRecord();
    }

    public async Task<bool> TryCreateAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Username))
            return false;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.UserAccounts.Add(new UserAccountEntity().Apply(account));
        return await StoreWrites.SaveIgnoringDuplicateAsync(db, cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var key = UserAccountEntity.KeyOf(account.Username);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.UserAccounts.FirstOrDefaultAsync(a => a.UsernameKey == key && a.Version == account.Version, cancellationToken);
        if (row is null)
            return false;

        row.Apply(account).Version++;
        try
        {
            return await db.SaveChangesAsync(cancellationToken) > 0;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public Task<UserAccount?> RecordFailedSignInAsync(string username, string passwordHash, int maxAttempts,
        DateTimeOffset lockoutUntil, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateSignInAsync($"""
            UPDATE scada.user_accounts
            SET failed_login_count = CASE WHEN failed_login_count >= {maxAttempts - 1} THEN 0 ELSE failed_login_count + 1 END,
                locked_until = CASE WHEN failed_login_count >= {maxAttempts - 1} THEN {Timestamps.Utc(lockoutUntil)} ELSE locked_until END,
                version = version + 1
            WHERE username_key = {UserAccountEntity.KeyOf(username)} AND password_hash = {passwordHash} AND is_enabled
                AND (locked_until IS NULL OR locked_until <= {Timestamps.Utc(now)})
            RETURNING *
            """, cancellationToken);

    public Task<UserAccount?> RecordSuccessfulSignInAsync(string username, string passwordHash, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        UpdateSignInAsync($"""
            UPDATE scada.user_accounts
            SET failed_login_count = 0, locked_until = NULL, last_login_at = greatest(last_login_at, {Timestamps.Utc(now)}),
                version = version + 1
            WHERE username_key = {UserAccountEntity.KeyOf(username)} AND password_hash = {passwordHash} AND is_enabled
                AND (locked_until IS NULL OR locked_until <= {Timestamps.Utc(now)})
            RETURNING *
            """, cancellationToken);

    private async Task<UserAccount?> UpdateSignInAsync(FormattableString update, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // Materialize directly so EF sends UPDATE ... RETURNING without composing a SELECT over it.
        var rows = await db.UserAccounts.FromSql(update).AsNoTracking().ToListAsync(cancellationToken);
        return rows.SingleOrDefault()?.ToRecord();
    }
}
