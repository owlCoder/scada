using Application.Common;
using Application.Incidents;
using Domain.Incidents;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

/// <summary>
/// Incident numbers come from a database sequence. Updates use <see cref="IncidentEntity.Version"/> as a concurrency
/// token, so a stale version fails even when two API instances race on the same incident.
/// </summary>
public sealed class PostgresIncidentStore(IDbContextFactory<ScadaDbContext> factory) : IIncidentStore
{
    /// <summary>Digits in <see cref="Incident.DisplayId"/>; the search below mirrors its INC-00042 format in SQL.</summary>
    private const int DisplayDigits = 5;

    public async Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return row?.ToRecord();
    }

    public async Task<Incident?> FindActiveByCorrelationAsync(string correlationKey, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Incidents.AsNoTracking()
            .Where(i => i.CorrelationKey == correlationKey && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToRecord();
    }

    public async Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        query = query.Normalize();
        var items = Filter(db.Incidents.AsNoTracking(), query);
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        var total = await items.CountAsync(cancellationToken);
        var rows = await Sort(items, query).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<Incident>(rows.Select(r => r.ToRecord()).ToArray(), page, size, total);
    }

    public async Task<Incident> AddAsync(Incident incident, IReadOnlyList<IncidentTimelineEntry> timeline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(timeline);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entity = IncidentEntity.From(incident);
        db.Incidents.Add(entity);
        db.IncidentTimeline.AddRange(timeline.Select(IncidentTimelineEntity.From));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (StoreWrites.IsUniqueViolation(exception))
        {
            throw new InvalidOperationException($"Incident {incident.Id} already exists.", exception);
        }

        return incident with { Number = entity.Number };
    }

    public async Task<bool> UpdateAsync(Incident incident, int expectedVersion, IReadOnlyList<IncidentTimelineEntry> timeline,
        CancellationToken cancellationToken, IncidentComment? comment = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(timeline);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incident.Id, cancellationToken);
        if (row is null || row.Version != expectedVersion)
            return false;

        row.Apply(incident);
        db.Entry(row).Property(r => r.Version).OriginalValue = expectedVersion;
        db.IncidentTimeline.AddRange(timeline.Select(IncidentTimelineEntity.From));
        if (comment is not null)
            db.IncidentComments.Add(IncidentCommentEntity.From(comment));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<bool> AddCommentAsync(IncidentComment comment, IncidentTimelineEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Incidents.AnyAsync(i => i.Id == comment.IncidentId, cancellationToken))
            return false;

        db.IncidentComments.Add(IncidentCommentEntity.From(comment));
        db.IncidentTimeline.Add(IncidentTimelineEntity.From(entry));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<IncidentComment>> GetCommentsAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.IncidentComments.AsNoTracking().Where(c => c.IncidentId == incidentId)
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    public async Task<IReadOnlyList<IncidentTimelineEntry>> GetTimelineAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.IncidentTimeline.AsNoTracking().Where(t => t.IncidentId == incidentId)
            .OrderBy(t => t.Timestamp).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    private static IQueryable<IncidentEntity> Filter(IQueryable<IncidentEntity> items, IncidentQuery query)
    {
        if (query.Status is { } status)
            items = items.Where(i => i.Status == status);
        if (query.Severity is { } severity)
            items = items.Where(i => i.Severity == severity);
        if (query.Category is { } category)
            items = items.Where(i => i.Category == category);
        if (query.ActiveOnly)
            items = items.Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignee = query.AssignedTo.Trim().ToLowerInvariant();
            items = items.Where(i => i.AssignedTo != null && i.AssignedTo.ToLower() == assignee);
        }

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            var device = query.DeviceId.Trim().ToUpperInvariant();
            items = items.Where(i => i.DeviceKeys.Contains(device));
        }

        if (query.ScenarioRunId is { } run)
            items = items.Where(i => i.ScenarioRunId == run);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search);
            items = items.Where(i => EF.Functions.ILike(i.Title, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(i.Description, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(i.Source, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike("INC-" + (i.Number < 100_000 ? i.Number.ToString().PadLeft(DisplayDigits, '0') : i.Number.ToString()),
                    pattern, LikePattern.EscapeCharacter));
        }

        return items;
    }

    private static IOrderedQueryable<IncidentEntity> Sort(IQueryable<IncidentEntity> items, IncidentQuery query) => query.SortBy switch
    {
        IncidentSortField.UpdatedAt => Order(items, i => i.UpdatedAt, query.Descending),
        IncidentSortField.Severity => Order(items, i => i.Severity, query.Descending),
        IncidentSortField.Priority => Order(items, i => i.Priority, query.Descending),
        IncidentSortField.Status => Order(items, i => i.Status, query.Descending),
        IncidentSortField.Number => Order(items, i => i.Number, query.Descending),
        _ => Order(items, i => i.CreatedAt, query.Descending)
    };

    private static IOrderedQueryable<IncidentEntity> Order<TKey>(IQueryable<IncidentEntity> items,
        System.Linq.Expressions.Expression<Func<IncidentEntity, TKey>> key, bool descending) =>
        descending ? items.OrderByDescending(key).ThenByDescending(i => i.Number) : items.OrderBy(key).ThenBy(i => i.Number);
}
