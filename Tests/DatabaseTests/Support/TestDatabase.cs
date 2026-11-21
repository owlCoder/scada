using Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DatabaseTests.Support;

public sealed record TestDatabase(string ConnectionString)
{
    public IDbContextFactory<ScadaDbContext> ContextFactory() =>
        new PooledDbContextFactory<ScadaDbContext>(
            ScadaDbContext.Configure(new DbContextOptionsBuilder<ScadaDbContext>(), ConnectionString).Options);

    public Task<IReadOnlyList<string>> MigrateAsync() =>
        new ScadaDatabaseMigrator(ContextFactory(), NullLogger<ScadaDatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);

    /// <summary>Migrates and returns a context factory, for store tests.</summary>
    public async Task<IDbContextFactory<ScadaDbContext>> MigratedAsync()
    {
        await MigrateAsync();
        return ContextFactory();
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
