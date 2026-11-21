using Domain.Process.Alarms;

namespace Application.Alarms;

public sealed record AlarmHistoryRecord(
    Guid Id,
    DateTimeOffset Timestamp,
    string AlarmKey,
    string SourceId,
    AlarmType Type,
    AlarmSeverity Severity,
    AlarmTransitionKind Transition,
    string Message,
    string? Actor);
