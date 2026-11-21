using Application.Detection;
using Domain.Incidents;
using Domain.Process.Commands;

namespace Security.Detection.Rules;

/// <summary>Pump state changes with no matching operator command, controller output, interlock or fail-safe action.</summary>
public sealed class UnexpectedPumpActivityRule : DetectionRuleBase
{
    public override string Id => "unexpected-pump-activity";
    public override string Name => "Unexpected pump activity";
    public override string Description => "A pump changed state without any attributable command.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 0, 60, 60);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings) =>
        context.Report.Plant.DeviceChanges
            .Where(c => c.Property == "State" && c.Cause is ChangeCause.Unattributed or ChangeCause.None
                && context.Current.Pump(c.DeviceId) is not null)
            .Select(c => new DetectionFinding(c.DeviceId, $"Unexpected activation of {c.DeviceId}",
                $"{c.DeviceId} changed {c.PreviousValue} -> {c.NewValue} with no matching command in the audit trail.",
                c.DeviceId, [Evidence("device-change", c.DeviceId, $"{c.PreviousValue} -> {c.NewValue}")]))
            .ToArray();
}
