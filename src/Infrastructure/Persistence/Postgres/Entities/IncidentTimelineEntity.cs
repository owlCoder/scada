using Domain.Incidents;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class IncidentTimelineEntity
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Actor { get; set; } = "";
    public IncidentTimelineEntryKind Kind { get; set; }
    public string Description { get; set; } = "";
    public IncidentStatus? FromStatus { get; set; }
    public IncidentStatus? ToStatus { get; set; }

    public static IncidentTimelineEntity From(IncidentTimelineEntry e) => new()
    {
        Id = e.Id, IncidentId = e.IncidentId, Timestamp = Timestamps.Utc(e.Timestamp), Actor = e.Actor, Kind = e.Kind,
        Description = e.Description, FromStatus = e.FromStatus, ToStatus = e.ToStatus
    };

    public IncidentTimelineEntry ToRecord() => new(Id, IncidentId, Timestamp, Actor, Kind, Description, FromStatus, ToStatus);
}
