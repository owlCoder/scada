using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Postgres;

/// <summary>Applies pending EF Core migrations. Runs once at startup, before accounts are seeded.</summary>
public sealed class ScadaDatabaseMigrator(IDbContextFactory<ScadaDbContext> factory, ILogger<ScadaDatabaseMigrator> logger)
{
    private const long MigrationLock = 0x5343414441;

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Keep the lock across the transactions managed by EF during migration.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({MigrationLock})", cancellationToken);
            return await ApplyPendingAsync(db, cancellationToken);
        }
        finally
        {
            if (db.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({MigrationLock})", CancellationToken.None);
        }
    }

    private async Task<IReadOnlyList<string>> ApplyPendingAsync(ScadaDbContext db, CancellationToken cancellationToken)
    {
        // Create the history table up front: on a fresh database EF would otherwise query the missing table and log a failed
        // command, which reads like a real error at every first start.
        var history = db.GetService<IHistoryRepository>();
        await history.CreateIfNotExistsAsync(cancellationToken);
        var applied = (await history.GetAppliedMigrationsAsync(cancellationToken)).Select(m => m.MigrationId).ToHashSet(StringComparer.Ordinal);
        var pending = db.Database.GetMigrations().Where(m => !applied.Contains(m)).ToArray();
        if (pending.Length == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return pending;
        }

        logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Length, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        return pending;
    }
}
