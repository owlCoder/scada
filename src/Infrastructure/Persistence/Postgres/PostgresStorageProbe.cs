using Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres;

public sealed class PostgresStorageProbe(IDbContextFactory<ScadaDbContext> factory) : IStorageProbe
{
    public string Provider => "postgres";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }
}
