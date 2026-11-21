using Application.Simulation;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

/// <summary>Keeps only the newest snapshot; an older snapshot never overwrites a newer one.</summary>
public sealed class PostgresPlantStateStore(IDbContextFactory<ScadaDbContext> factory) : IPlantStateStore
{
    public async Task<bool> SaveAsync(SimulationStateSnapshot state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.PlantState.FirstOrDefaultAsync(p => p.Id == PlantStateEntity.SingletonId, cancellationToken);
        if (row is null)
        {
            db.PlantState.Add(new PlantStateEntity().Apply(state));
        }
        else
        {
            if (row.SavedAt > Timestamps.Utc(state.SavedAt))
                return false;
            row.Apply(state);
        }

        return await StoreWrites.SaveIgnoringDuplicateAsync(db, cancellationToken);
    }

    public async Task<SimulationStateSnapshot?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.PlantState.AsNoTracking().FirstOrDefaultAsync(p => p.Id == PlantStateEntity.SingletonId, cancellationToken);
        return row?.ToRecord();
    }
}
