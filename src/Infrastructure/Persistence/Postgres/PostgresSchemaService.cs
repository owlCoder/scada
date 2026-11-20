using Npgsql;

namespace Infrastructure;

public sealed class PostgresSchemaService(NpgsqlDataSource source)
{
    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                to_regclass('public.telemetry_samples') IS NOT NULL
                AND to_regclass('public.security_events') IS NOT NULL
                AND to_regclass('public.incidents') IS NOT NULL
            """, connection);

        var ready = (bool?)await command.ExecuteScalarAsync(cancellationToken);
        if (ready != true)
            throw new InvalidOperationException(
                "PostgreSQL шема није потпуна. Примени deploy/postgres/init.sql пре покретања API-ја.");
    }
}
