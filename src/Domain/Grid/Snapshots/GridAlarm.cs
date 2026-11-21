using Domain.Process.Alarms;

namespace Domain.Grid.Snapshots;

public sealed record GridAlarm(
    string Key,
    string SourceId,
    GridAlarmType Type,
    AlarmSeverity Severity,
    string Message,
    TimeSpan RaisedAt,
    bool Active,
    bool Acknowledged,
    string? AcknowledgedBy);
