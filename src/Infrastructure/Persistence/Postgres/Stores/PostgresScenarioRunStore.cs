using Application.Scenarios;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresScenarioRunStore(IDbContextFactory<ScadaDbContext> factory) : IScenarioRunStore
{
    private const int MaxAttempts = 3;
    private const int MaxList = 1000;

    public async Task<bool> SaveAsync(ScenarioRunRecord run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var row = await db.ScenarioRuns.FirstOrDefaultAsync(r => r.RunId == run.RunId, cancellationToken);
            if (row is null)
                db.ScenarioRuns.Add(new ScenarioRunEntity().Apply(run));
            else
                row.Apply(run);

            if (await StoreWrites.SaveIgnoringDuplicateAsync(db, cancellationToken))
                return true;
        }

        return false;
    }

    public async Task<ScenarioRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.ScenarioRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);
        return row?.ToRecord();
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<ScenarioRunRecord>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ScenarioRuns.AsNoTracking().OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, MaxList)).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }
}
