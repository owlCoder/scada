using Application.Events;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class EventEntity
{
    public long Position { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Category { get; set; } = "";
    public EventSeverity Severity { get; set; }
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? DeviceId { get; set; }
    public Guid? ScenarioRunId { get; set; }
    public string? CorrelationId { get; set; }

    public static EventEntity From(SecurityEvent e) => new()
    {
        Id = e.Id,
        Timestamp = Timestamps.Utc(e.Timestamp),
        Category = e.Category,
        Severity = e.Severity,
        Source = e.Source,
        Message = e.Message,
        Actor = e.Actor,
        DeviceId = e.DeviceId,
        ScenarioRunId = e.ScenarioRunId,
        CorrelationId = e.CorrelationId
    };

    public SecurityEvent ToRecord() =>
        new(Id, Timestamp, Category, Severity, Source, Message, Actor, DeviceId, ScenarioRunId, CorrelationId);
}
