namespace Domain.Process.Alarms;

public sealed record AlarmCondition(string Key, string SourceId, AlarmType Type, AlarmSeverity Severity, string Message);
