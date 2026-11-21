using Application.Detection;
using Application.Persistence;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresDetectionStore(IDbContextFactory<ScadaDbContext> factory) : IDetectionStore, IRetentionTarget
{
    public string Name => "detections";

    public async Task<bool> AppendAsync(DetectionRecord detection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detection);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Detections.Add(DetectionEntity.From(detection));
        return await db.SaveChangesAsync(cancellationToken) > 0;
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<DetectionRecord>> QueryAsync(DetectionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var items = db.Detections.AsNoTracking();
        if (query.RuleId is { } rule)
            items = items.Where(d => d.RuleId == rule);
        if (query.ScenarioRunId is { } run)
            items = items.Where(d => d.ScenarioRunId == run);
        if (query.Since is { } since)
            items = items.Where(d => d.Timestamp >= Timestamps.Utc(since));

        var rows = await items.OrderByDescending(d => d.Timestamp).ThenByDescending(d => d.Position)
            .Take(Math.Clamp(query.Limit, 1, 5000)).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    public async Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var cutoff = Timestamps.Utc(olderThan);
        return await db.Detections.Where(d => d.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
