using Application.Grid;
using Domain.Grid.Definition;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

/// <summary>A database transaction and table lock serialize version allocation across instances.</summary>
public sealed class PostgresGridDefinitionStore(IDbContextFactory<ScadaDbContext> factory, TimeProvider time) : IGridDefinitionStore
{
    public async Task<GridDefinitionVersion?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await EnsureSeedAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.GridDefinitions.AsNoTracking().SingleAsync(x => x.Active, cancellationToken);
        return Map(row);
    }
    public async Task<GridDefinitionVersion?> GetAsync(int version, CancellationToken cancellationToken)
    {
        await EnsureSeedAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.GridDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Version == version, cancellationToken);
        return row is null ? null : Map(row);
    }
    public async Task<IReadOnlyList<GridDefinitionVersion>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureSeedAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.GridDefinitions.AsNoTracking().OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }
    public async Task<GridDefinitionVersion> SaveAsync(GridDefinition definition, string createdBy, string comment, CancellationToken cancellationToken)
    {
        await EnsureSeedAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE scada.grid_definitions IN EXCLUSIVE MODE", cancellationToken);
        var next = await db.GridDefinitions.MaxAsync(x => x.Version, cancellationToken) + 1;
        var row = new GridDefinitionEntity { Version = next, Definition = PersistenceJson.Serialize(definition), CreatedBy = createdBy,
            CreatedAt = time.GetUtcNow(), Comment = comment, Active = false };
        db.GridDefinitions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(row);
    }
    public async Task<bool> SetActiveAsync(int version, CancellationToken cancellationToken)
    {
        await EnsureSeedAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE scada.grid_definitions IN EXCLUSIVE MODE", cancellationToken);
        if (!await db.GridDefinitions.AnyAsync(x => x.Version == version, cancellationToken)) return false;
        await db.GridDefinitions.Where(x => x.Active).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false), cancellationToken);
        await db.GridDefinitions.Where(x => x.Version == version).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, true), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    private async Task<bool> EnsureSeedAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.GridDefinitions.AnyAsync(cancellationToken)) return false;
        var json = PersistenceJson.Serialize(DefaultGridDefinition.Create());
        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO scada.grid_definitions (version, definition, created_by, created_at, comment, active) VALUES (1, CAST({json} AS jsonb), 'system', {now}, 'Default distribution network', TRUE) ON CONFLICT (version) DO NOTHING", cancellationToken);
        return true;
    }
    private static GridDefinitionVersion Map(GridDefinitionEntity row) => new(row.Version, PersistenceJson.Deserialize<GridDefinition>(row.Definition),
        row.CreatedBy, row.CreatedAt, row.Comment, row.Active);
}
