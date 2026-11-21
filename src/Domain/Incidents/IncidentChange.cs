namespace Domain.Incidents;

public sealed record IncidentChange(
    IncidentOperationStatus Status,
    Incident? Incident,
    IReadOnlyList<IncidentTimelineEntry> Timeline,
    string Message)
{
    public bool Succeeded => Status == IncidentOperationStatus.Succeeded;

    public static IncidentChange Failure(IncidentOperationStatus status, string message) => new(status, null, [], message);
}
