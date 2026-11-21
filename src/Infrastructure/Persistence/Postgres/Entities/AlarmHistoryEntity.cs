using Application.Alarms;
using Domain.Process.Alarms;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class AlarmHistoryEntity
{
    public long Position { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string AlarmKey { get; set; } = "";
    public string SourceId { get; set; } = "";
    public AlarmType Type { get; set; }
    public AlarmSeverity Severity { get; set; }
    public AlarmTransitionKind Transition { get; set; }
    public string Message { get; set; } = "";
    public string? Actor { get; set; }

    public static AlarmHistoryEntity From(AlarmHistoryRecord r) => new()
    {
        Id = r.Id,
        Timestamp = Timestamps.Utc(r.Timestamp),
        AlarmKey = r.AlarmKey,
        SourceId = r.SourceId,
        Type = r.Type,
        Severity = r.Severity,
        Transition = r.Transition,
        Message = r.Message,
        Actor = r.Actor
    };

    public AlarmHistoryRecord ToRecord() => new(Id, Timestamp, AlarmKey, SourceId, Type, Severity, Transition, Message, Actor);
}
