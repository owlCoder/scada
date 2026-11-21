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
        var row = DetectionEntity.From(detection);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scada.detections (id, timestamp, rule_id, rule_name, severity, category, title, description,
                device_id, scenario_run_id, fingerprint, evidence, incident_id)
            VALUES ({row.Id}, {row.Timestamp}, {row.RuleId}, {row.RuleName}, {row.Severity.ToString()},
                {row.Category.ToString()}, {row.Title}, {row.Description}, {row.DeviceId}, {row.ScenarioRunId},
                {row.Fingerprint}, CAST({row.Evidence} AS jsonb), {row.IncidentId})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken) > 0;
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
