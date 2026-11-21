using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

public sealed class ReplayAttackRule : EventThresholdRule
{
    public override string Id => "command-replay";
    public override string Name => "Command replay";
    public override string Description => "Control commands reusing an already accepted sequence number.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 1, 300, 300);
    protected override string[] Categories => [EventCategories.SecurityAlert];

    protected override bool Matches(SecurityEvent e) => e.Message.Contains("(ReplayDetected)", StringComparison.Ordinal);

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Replayed control commands for {group}",
            $"{events.Count} command(s) reused stale sequence numbers under identity {group}.",
            events[^1].DeviceId, EventEvidence(events));
}
