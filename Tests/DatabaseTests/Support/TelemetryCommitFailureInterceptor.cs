using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DatabaseTests.Support;

/// <summary>Simulates an ambiguous commit: the INSERT succeeded, but its acknowledgement was lost.</summary>
public sealed class TelemetryCommitFailureInterceptor : DbCommandInterceptor
{
    public bool Failed { get; private set; }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        if (!Failed && command.CommandText.Contains("INSERT INTO scada.telemetry", StringComparison.Ordinal))
        {
            Failed = true;
            throw new InvalidOperationException("lost commit acknowledgement");
        }
        return ValueTask.FromResult(result);
    }
}
