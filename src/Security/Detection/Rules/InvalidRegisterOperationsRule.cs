using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

public sealed class InvalidRegisterOperationsRule : EventThresholdRule
{
    public override string Id => "invalid-register-operations";
    public override string Name => "Invalid register operations";
    public override string Description => "Repeated illegal-address, illegal-value or unauthorised register operations from one source.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 3, 120, 300);
    protected override string[] Categories => [EventCategories.Protocol, EventCategories.AuthorizationDenied];

    protected override bool Matches(SecurityEvent e) =>
        e.Category == EventCategories.Protocol || e.Message.Contains("register", StringComparison.OrdinalIgnoreCase);

    protected override string GroupKey(SecurityEvent e) => e.Source;

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Invalid register operations from {group}",
            $"{events.Count} invalid or unauthorised register operations from {group} within {settings.WindowSeconds}s.",
            events.LastOrDefault(e => e.DeviceId is not null)?.DeviceId, EventEvidence(events));
}
