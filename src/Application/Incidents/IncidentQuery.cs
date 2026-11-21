using Application.Common;
using Domain.Incidents;

namespace Application.Incidents;

public sealed record IncidentQuery(
    IncidentStatus? Status = null,
    IncidentSeverity? Severity = null,
    IncidentCategory? Category = null,
    string? Search = null,
    string? AssignedTo = null,
    string? DeviceId = null,
    Guid? ScenarioRunId = null,
    bool ActiveOnly = false,
    IncidentSortField SortBy = IncidentSortField.CreatedAt,
    bool Descending = true,
    int Page = 1,
    int PageSize = 25)
{
    public IncidentQuery Normalize() => this with
    {
        Search = QueryText.Normalize(Search),
        AssignedTo = QueryText.Normalize(AssignedTo),
        DeviceId = QueryText.Normalize(DeviceId)
    };
}
