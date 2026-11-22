using Domain.Process.Alarms;

namespace Hmi.Services.Shell;

/// <summary>Mode-neutral alarm row for the shell alarm bar (water and grid alarms share the ISA-18.2 severity scale).</summary>
public sealed record ShellAlarmItem(
    string Key,
    string SourceId,
    AlarmSeverity Severity,
    string Message,
    string RaisedAt,
    bool Active,
    bool Acknowledged,
    string? AcknowledgedBy)
{
    /// <summary>ISA-18.2 priority class for the design tokens: Critical = P1 … Low = P4.</summary>
    public string PriorityClass => Severity switch
    {
        AlarmSeverity.Critical => "p1",
        AlarmSeverity.High => "p2",
        AlarmSeverity.Medium => "p3",
        _ => "p4",
    };

    public static string PriorityLabel(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.Critical => "P1",
        AlarmSeverity.High => "P2",
        AlarmSeverity.Medium => "P3",
        _ => "P4",
    };
}
