using Domain.Incidents;

namespace Application.Incidents;

public sealed record IncidentDetails(
    Incident Incident,
    IReadOnlyList<IncidentComment> Comments,
    IReadOnlyList<IncidentTimelineEntry> Timeline,
    IReadOnlyList<IncidentStatus> AllowedTransitions);
