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
        var row = new PlantStateEntity().Apply(state);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scada.plant_state (id, saved_at, is_paused, plant, controllers)
            VALUES ({row.Id}, {row.SavedAt}, {row.IsPaused}, CAST({row.Plant} AS jsonb), CAST({row.Controllers} AS jsonb))
            ON CONFLICT (id) DO UPDATE SET saved_at = excluded.saved_at, is_paused = excluded.is_paused,
                plant = excluded.plant, controllers = excluded.controllers
            WHERE plant_state.saved_at <= excluded.saved_at
            """, cancellationToken) > 0;
    }

    public async Task<SimulationStateSnapshot?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.PlantState.AsNoTracking().FirstOrDefaultAsync(p => p.Id == PlantStateEntity.SingletonId, cancellationToken);
        return row?.ToRecord();
    }
}
