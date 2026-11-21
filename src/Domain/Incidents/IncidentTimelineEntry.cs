namespace Domain.Incidents;

public sealed record IncidentTimelineEntry(
    Guid Id,
    Guid IncidentId,
    DateTimeOffset Timestamp,
    string Actor,
    IncidentTimelineEntryKind Kind,
    string Description,
    IncidentStatus? FromStatus,
    IncidentStatus? ToStatus);
