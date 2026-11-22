using Domain.Process.Alarms;

namespace Hmi.Services;

/// <summary>One alarm bell on the P&amp;ID: position plus the data needed to render it (priority colour, blink while unacknowledged).</summary>
public sealed record PidAlarmBell(string SourceId, double X, double Y, AlarmSeverity Severity, bool Acknowledged, string Message);
