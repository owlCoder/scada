using Application.Events;
using Domain.Control;
using Domain.Process.Alarms;
using Domain.Process.Commands;

namespace Application.Simulation;

/// <summary>Turns a step report into audit, process and communication events.</summary>
public static class StepEventTranslator
{
    public static IReadOnlyList<SecurityEvent> Translate(SimulationStepReport report)
    {
        var now = report.Timestamp;
        var events = new List<SecurityEvent>();

        foreach (var transition in report.Plant.AlarmTransitions.Where(t => t.Kind != AlarmTransitionKind.Acknowledged))
        {
            var alarm = transition.Alarm;
            var message = transition.Kind == AlarmTransitionKind.Raised ? alarm.Message : $"{alarm.Message} cleared";
            events.Add(SecurityEvent.Create(now, EventCategories.ProcessAlarm,
                transition.Kind == AlarmTransitionKind.Raised ? Map(alarm.Severity) : EventSeverity.Information,
                "simulation", message, "simulation", alarm.SourceId));
        }

        foreach (var interlock in report.Plant.Interlocks)
            events.Add(SecurityEvent.Create(now, EventCategories.SafetyInterlock, EventSeverity.High, "safety-system",
                $"Interlock on {interlock.DeviceId}: {interlock.Reason}", "safety-system", interlock.DeviceId));

        foreach (var violation in report.Plant.SafetyViolations)
            events.Add(SecurityEvent.Create(now, EventCategories.SafetyViolation, EventSeverity.Critical, "safety-system",
                $"Safety violation on {violation.DeviceId}: {violation.Description}", "safety-system", violation.DeviceId));

        foreach (var change in report.Plant.DeviceChanges)
            events.Add(SecurityEvent.Create(now, EventCategories.DeviceChange,
                change.Cause == ChangeCause.Unattributed ? EventSeverity.Medium : EventSeverity.Information,
                change.Cause.ToString(), $"{change.DeviceId} {change.Property}: {change.PreviousValue} -> {change.NewValue}",
                change.Actor, change.DeviceId));

        foreach (var scan in report.Scans)
        {
            if (scan.CommunicationLost)
                events.Add(SecurityEvent.Create(now, EventCategories.Communication, EventSeverity.High, scan.ControllerId,
                    $"{scan.ControllerId} lost communication with its field I/O", scan.ControllerId));
            if (scan.EnteredSafeState)
                events.Add(SecurityEvent.Create(now, EventCategories.Controller, EventSeverity.High, scan.ControllerId,
                    $"{scan.ControllerId} entered safe state; {scan.FailSafeDevices.Count} actuators driven to fail-safe", scan.ControllerId));
            if (scan.CommunicationRestored)
                events.Add(SecurityEvent.Create(now, EventCategories.Communication, EventSeverity.Information, scan.ControllerId,
                    $"{scan.ControllerId} field communication restored", scan.ControllerId));
        }

        foreach (var change in report.SupervisoryChanges.Where(c => c.Current != CommunicationState.Degraded || c.Previous == CommunicationState.Online))
            events.Add(SecurityEvent.Create(now, EventCategories.Communication,
                change.Current == CommunicationState.Online ? EventSeverity.Information : EventSeverity.Medium,
                "scada", $"Supervisory link to {change.ControllerId}: {change.Previous} -> {change.Current}", "scada", change.ControllerId));

        return events;
    }

    private static EventSeverity Map(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.Critical => EventSeverity.Critical,
        AlarmSeverity.High => EventSeverity.High,
        AlarmSeverity.Medium => EventSeverity.Medium,
        _ => EventSeverity.Low
    };
}
