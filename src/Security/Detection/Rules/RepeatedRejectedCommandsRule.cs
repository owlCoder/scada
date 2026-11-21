using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

public sealed class RepeatedRejectedCommandsRule : EventThresholdRule
{
    public override string Id => "repeated-rejected-commands";
    public override string Name => "Repeated rejected commands";
    public override string Description => "Several control commands from one identity were rejected within the window.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 3, 120, 300);
    protected override string[] Categories => [EventCategories.CommandRejected, EventCategories.AuthorizationDenied];

    protected override bool Matches(SecurityEvent securityEvent) =>
        securityEvent.Category == EventCategories.CommandRejected || securityEvent.Message.Contains("rejected", StringComparison.Ordinal);

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Repeated rejected commands from {group}",
            $"{events.Count} commands from {group} were rejected within {settings.WindowSeconds}s.",
            events[^1].DeviceId, EventEvidence(events));
}
