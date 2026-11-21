namespace Domain.Incidents;

public sealed record Incident(
    Guid Id,
    long Number,
    string Title,
    string Description,
    IncidentCategory Category,
    IncidentSeverity Severity,
    IncidentPriority Priority,
    string Source,
    IncidentStatus Status,
    string? AssignedTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNotes,
    IReadOnlyList<string> DeviceIds,
    Guid? ScenarioRunId,
    string CorrelationKey,
    IReadOnlyList<IncidentEvidence> Evidence,
    int Version)
{
    public string DisplayId => Number > 0 ? $"INC-{Number:D5}" : "INC-PENDING";
    public bool IsActive => Status is not (IncidentStatus.Resolved or IncidentStatus.Closed);
}
