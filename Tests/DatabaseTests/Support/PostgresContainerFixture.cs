using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DatabaseTests.Support;

/// <summary>
/// One PostgreSQL container per test run; every test gets its own database. If Docker is not available the fixture
/// throws, so every database test fails loudly instead of being skipped.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public const string Image = "postgres:17.7-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"DatabaseTests need a working Docker engine to start {Image} with Testcontainers: {exception.Message}",
                exception);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        return new TestDatabase(builder.ConnectionString);
    }
}
