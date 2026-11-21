using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

public sealed class NetworkSegmentationRule : EventThresholdRule
{
    public override string Id => "network-segmentation-violation";
    public override string Name => "Network segmentation violation";
    public override string Description => "Connection attempts across zones without an allowed conduit.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 1, 300, 300);
    protected override string[] Categories => [EventCategories.Network];

    protected override bool Matches(SecurityEvent e) => e.Message.StartsWith("Blocked connection", StringComparison.Ordinal);

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Blocked cross-zone connections from {group}",
            $"{events.Count} connection attempt(s) from {group} violated the zone and conduit model.",
            events[^1].DeviceId, EventEvidence(events));
}
