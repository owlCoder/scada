using Application.Common;
using Domain.Incidents;

namespace Application.Incidents;

/// <summary>In-memory implementation of incident filtering, sorting and paging.</summary>
public static class IncidentQueryEvaluator
{
    public static PagedResult<Incident> Apply(IEnumerable<Incident> source, IncidentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = source;
        if (query.Status is { } status)
            items = items.Where(i => i.Status == status);
        if (query.Severity is { } severity)
            items = items.Where(i => i.Severity == severity);
        if (query.Category is { } category)
            items = items.Where(i => i.Category == category);
        if (query.ActiveOnly)
            items = items.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
            items = items.Where(i => string.Equals(i.AssignedTo, query.AssignedTo, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.DeviceId))
            items = items.Where(i => i.DeviceIds.Contains(query.DeviceId, StringComparer.OrdinalIgnoreCase));
        if (query.ScenarioRunId is { } run)
            items = items.Where(i => i.ScenarioRunId == run);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(i => i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.DisplayId.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Source.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Func<Incident, object> key = query.SortBy switch
        {
            IncidentSortField.UpdatedAt => i => i.UpdatedAt,
            IncidentSortField.Severity => i => i.Severity,
            IncidentSortField.Priority => i => i.Priority,
            IncidentSortField.Status => i => i.Status,
            IncidentSortField.Number => i => i.Number,
            _ => i => i.CreatedAt
        };
        var ordered = query.Descending
            ? items.OrderByDescending(key).ThenByDescending(i => i.Number)
            : items.OrderBy(key).ThenBy(i => i.Number);
        return PagedResult<Incident>.From(ordered, query.Page, query.PageSize);
    }
}
