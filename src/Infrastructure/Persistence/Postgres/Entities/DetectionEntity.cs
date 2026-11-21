using Application.Detection;
using Domain.Incidents;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class DetectionEntity
{
    public long Position { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public DetectionSeverity Severity { get; set; }
    public IncidentCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DeviceId { get; set; }
    public Guid? ScenarioRunId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Evidence { get; set; } = "[]";
    public Guid? IncidentId { get; set; }

    public static DetectionEntity From(DetectionRecord d) => new()
    {
        Id = d.Id, Timestamp = Timestamps.Utc(d.Timestamp), RuleId = d.RuleId, RuleName = d.RuleName, Severity = d.Severity,
        Category = d.Category, Title = d.Title, Description = d.Description, DeviceId = d.DeviceId, ScenarioRunId = d.ScenarioRunId,
        Fingerprint = d.Fingerprint, Evidence = PersistenceJson.Serialize(d.Evidence), IncidentId = d.IncidentId
    };

    public DetectionRecord ToRecord() => new(Id, Timestamp, RuleId, RuleName, Severity, Category, Title, Description, DeviceId,
        ScenarioRunId, Fingerprint, PersistenceJson.Deserialize<DetectionEvidence[]>(Evidence), IncidentId);
}
