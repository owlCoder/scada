using Domain.Incidents;

namespace Application.Detection;

public sealed record DetectionRecord(
    Guid Id,
    DateTimeOffset Timestamp,
    string RuleId,
    string RuleName,
    DetectionSeverity Severity,
    IncidentCategory Category,
    string Title,
    string Description,
    string? DeviceId,
    Guid? ScenarioRunId,
    string Fingerprint,
    IReadOnlyList<DetectionEvidence> Evidence,
    Guid? IncidentId);
