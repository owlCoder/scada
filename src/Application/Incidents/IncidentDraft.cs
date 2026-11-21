using Domain.Incidents;

namespace Application.Incidents;

public sealed record IncidentDraft(
    string Title,
    string Description,
    IncidentCategory Category,
    IncidentSeverity Severity,
    string Source,
    string CorrelationKey,
    IReadOnlyList<string> DeviceIds,
    Guid? ScenarioRunId,
    IncidentEvidence Evidence);
