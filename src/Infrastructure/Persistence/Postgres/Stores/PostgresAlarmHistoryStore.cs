using Application.Alarms;
using Application.Persistence;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresAlarmHistoryStore(IDbContextFactory<ScadaDbContext> factory) : IAlarmHistoryStore, IRetentionTarget
{
    private const int MaxCount = 5000;

    public string Name => "alarm-history";

    public async Task<int> AppendAsync(IReadOnlyList<AlarmHistoryRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            return 0;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = records.Select(AlarmHistoryEntity.From).ToArray();
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scada.alarm_history (id, timestamp, alarm_key, source_id, type, severity, transition, message, actor)
            SELECT id, timestamp, alarm_key, source_id, type, severity, transition, message, actor
            FROM unnest({rows.Select(a => a.Id).ToArray()}, {rows.Select(a => a.Timestamp).ToArray()},
                {rows.Select(a => a.AlarmKey).ToArray()}, {rows.Select(a => a.SourceId).ToArray()},
                {rows.Select(a => a.Type.ToString()).ToArray()}, {rows.Select(a => a.Severity.ToString()).ToArray()},
                {rows.Select(a => a.Transition.ToString()).ToArray()}, {rows.Select(a => a.Message).ToArray()},
                {rows.Select(a => a.Actor).ToArray()})
                WITH ORDINALITY AS input(id, timestamp, alarm_key, source_id, type, severity, transition, message,
                    actor, input_position)
            ORDER BY input_position
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<AlarmHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.AlarmHistory.AsNoTracking().OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Position)
            .Take(Math.Clamp(count, 1, MaxCount)).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    public async Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var cutoff = Timestamps.Utc(olderThan);
        return await db.AlarmHistory.Where(a => a.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
