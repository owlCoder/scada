using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Postgres.Stores;

public static class StoreWrites
{
    /// <summary>
    /// Saves pending changes. A unique-key violation means a concurrent writer inserted the same row first; that is
    /// reported as <c>false</c> so callers can retry or treat the write as superseded.
    /// </summary>
    public static async Task<bool> SaveIgnoringDuplicateAsync(DbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return false;
        }
    }

    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
