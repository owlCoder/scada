using Application.Common;
using Application.Events;
using Application.Persistence;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresEventStore(IDbContextFactory<ScadaDbContext> factory) : IEventStore, IRetentionTarget
{
    public string Name => "events";

    public async Task<int> AppendAsync(IReadOnlyList<SecurityEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return 0;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = events.Select(EventEntity.From).ToArray();
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scada.events (id, timestamp, category, severity, source, message, actor, device_id, scenario_run_id, correlation_id)
            SELECT id, timestamp, category, severity, source, message, actor, device_id, scenario_run_id, correlation_id
            FROM unnest({rows.Select(e => e.Id).ToArray()}, {rows.Select(e => e.Timestamp).ToArray()},
                {rows.Select(e => e.Category).ToArray()}, {rows.Select(e => e.Severity.ToString()).ToArray()},
                {rows.Select(e => e.Source).ToArray()}, {rows.Select(e => e.Message).ToArray()},
                {rows.Select(e => e.Actor).ToArray()}, {rows.Select(e => e.DeviceId).ToArray()},
                {rows.Select(e => e.ScenarioRunId).ToArray()}, {rows.Select(e => e.CorrelationId).ToArray()})
                WITH ORDINALITY AS input(id, timestamp, category, severity, source, message, actor, device_id,
                    scenario_run_id, correlation_id, input_position)
            ORDER BY input_position
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);
    }

    public async Task<PagedResult<SecurityEvent>> QueryAsync(EventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var items = db.Events.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Category))
            items = items.Where(e => e.Category.ToLower() == query.Category.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.Actor))
            items = items.Where(e => e.Actor.ToLower() == query.Actor.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.DeviceId))
            items = items.Where(e => e.DeviceId != null && e.DeviceId.ToLower() == query.DeviceId.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search);
            items = items.Where(e => EF.Functions.ILike(e.Message, pattern, LikePattern.EscapeCharacter));
        }

        if (query.From is { } from)
            items = items.Where(e => e.Timestamp >= Timestamps.Utc(from));
        if (query.To is { } to)
            items = items.Where(e => e.Timestamp <= Timestamps.Utc(to));

        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        var total = await items.CountAsync(cancellationToken);
        var rows = await items.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Position)
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<SecurityEvent>(rows.Select(r => r.ToRecord()).ToArray(), page, size, total);
    }

    public async Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var cutoff = Timestamps.Utc(olderThan);
        return await db.Events.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
