namespace Domain.Process.Alarms;

public sealed record AlarmTransition(ProcessAlarm Alarm, AlarmTransitionKind Kind);
