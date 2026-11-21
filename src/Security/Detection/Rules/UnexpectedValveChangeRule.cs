using Application.Detection;
using Domain.Incidents;
using Domain.Process.Commands;

namespace Security.Detection.Rules;

public sealed class UnexpectedValveChangeRule : DetectionRuleBase
{
    public override string Id => "unexpected-valve-change";
    public override string Name => "Unexpected valve change";
    public override string Description => "A valve moved without any attributable command.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 0, 60, 60);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings) =>
        context.Report.Plant.DeviceChanges
            .Where(c => c.Property == "Setpoint" && c.Cause is ChangeCause.Unattributed or ChangeCause.None
                && context.Current.Valve(c.DeviceId) is not null)
            .Select(c => new DetectionFinding(c.DeviceId, $"Unexpected movement of {c.DeviceId}",
                $"{c.DeviceId} setpoint changed {c.PreviousValue}% -> {c.NewValue}% with no matching command in the audit trail.",
                c.DeviceId, [Evidence("device-change", c.DeviceId, $"{c.PreviousValue}% -> {c.NewValue}%")]))
            .ToArray();
}
