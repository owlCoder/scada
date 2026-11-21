namespace Domain.Process.Alarms;

public sealed record ProcessAlarm(
    string Key,
    string SourceId,
    AlarmType Type,
    AlarmSeverity Severity,
    string Message,
    bool IsActive,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ClearedAt,
    bool IsAcknowledged,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt);
