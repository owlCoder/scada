using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DatabaseTests.Support;

public sealed class MigrationFailureInterceptor : DbCommandInterceptor
{
    public bool UnlockAttempted { get; private set; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("pg_advisory_unlock", StringComparison.Ordinal))
        {
            UnlockAttempted = true;
            throw new InvalidOperationException("unlock failure");
        }
        if (!command.CommandText.Contains("pg_advisory_lock", StringComparison.Ordinal))
            throw new InvalidOperationException("original migration failure");
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("original migration failure");
}
