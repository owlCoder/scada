using Domain.Incidents;

namespace Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// Severity, priority and status are stored as integers so that sorting follows the enum order, as it does in memory.
/// <see cref="DeviceKeys"/> holds upper-cased device ids for case-insensitive filtering.
/// </summary>
public sealed class IncidentEntity
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public IncidentCategory Category { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentPriority Priority { get; set; }
    public string Source { get; set; } = "";
    public IncidentStatus Status { get; set; }
    public string? AssignedTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<string> DeviceIds { get; set; } = [];
    public List<string> DeviceKeys { get; set; } = [];
    public Guid? ScenarioRunId { get; set; }
    public string CorrelationKey { get; set; } = "";
    public string Evidence { get; set; } = "[]";
    public int Version { get; set; }

    public static IncidentEntity From(Incident incident) => new IncidentEntity { Id = incident.Id }.Apply(incident);

    /// <summary>Copies every field except the id and the number, which never change after insert.</summary>
    public IncidentEntity Apply(Incident i)
    {
        Title = i.Title;
        Description = i.Description;
        Category = i.Category;
        Severity = i.Severity;
        Priority = i.Priority;
        Source = i.Source;
        Status = i.Status;
        AssignedTo = i.AssignedTo;
        CreatedAt = Timestamps.Utc(i.CreatedAt);
        UpdatedAt = Timestamps.Utc(i.UpdatedAt);
        AcknowledgedBy = i.AcknowledgedBy;
        AcknowledgedAt = Timestamps.Utc(i.AcknowledgedAt);
        ResolvedBy = i.ResolvedBy;
        ResolvedAt = Timestamps.Utc(i.ResolvedAt);
        ResolutionNotes = i.ResolutionNotes;
        DeviceIds = [.. i.DeviceIds];
        DeviceKeys = [.. i.DeviceIds.Select(d => d.ToUpperInvariant())];
        ScenarioRunId = i.ScenarioRunId;
        CorrelationKey = i.CorrelationKey;
        Evidence = PersistenceJson.Serialize(i.Evidence);
        Version = i.Version;
        return this;
    }

    public Incident ToRecord() => new(Id, Number, Title, Description, Category, Severity, Priority, Source, Status, AssignedTo,
        CreatedAt, UpdatedAt, AcknowledgedBy, AcknowledgedAt, ResolvedBy, ResolvedAt, ResolutionNotes, DeviceIds.ToArray(),
        ScenarioRunId, CorrelationKey, PersistenceJson.Deserialize<IncidentEvidence[]>(Evidence), Version);
}
