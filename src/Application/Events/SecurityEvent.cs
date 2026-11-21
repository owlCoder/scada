namespace Application.Events;

/// <summary>An audit, process or security event. Category values are listed in <see cref="EventCategories"/>.</summary>
public sealed record SecurityEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    string Category,
    EventSeverity Severity,
    string Source,
    string Message,
    string Actor,
    string? DeviceId = null,
    Guid? ScenarioRunId = null,
    string? CorrelationId = null)
{
    public static SecurityEvent Create(DateTimeOffset timestamp, string category, EventSeverity severity, string source,
        string message, string actor, string? deviceId = null, Guid? scenarioRunId = null) =>
        new(Guid.NewGuid(), timestamp, category, severity, source, message, actor, deviceId, scenarioRunId);
}
