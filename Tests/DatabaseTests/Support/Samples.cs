using Application.Events;
using Domain.Incidents;

namespace DatabaseTests.Support;

public static class Samples
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static Incident Incident(string title = "Tank overflow", IncidentSeverity severity = IncidentSeverity.High,
        IncidentStatus status = IncidentStatus.Open, string[]? devices = null, string correlation = "rule:tank", int minutes = 0) =>
        new(Guid.NewGuid(), 0, title, $"{title} detected", IncidentCategory.ProcessSafety, severity, IncidentPriority.Normal,
            "detection", status, null, Start.AddMinutes(minutes), Start.AddMinutes(minutes), null, null, null, null, null,
            devices ?? ["T-101"], null, correlation, [new IncidentEvidence(Start, "sensor", "LT-101", "Level 99 %")], 1);

    public static IncidentTimelineEntry Created(Incident incident) =>
        new(Guid.NewGuid(), incident.Id, incident.CreatedAt, "system", IncidentTimelineEntryKind.Created, "Created", null, IncidentStatus.Open);

    public static SecurityEvent Event(string message, string category = "Command", int seconds = 0, string actor = "ana") =>
        new(Guid.NewGuid(), Start.AddSeconds(seconds), category, EventSeverity.Low, "api", message, actor, "P-101");
}
