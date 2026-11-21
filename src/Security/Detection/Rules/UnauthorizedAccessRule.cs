using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

/// <summary>Failed authentication and authorisation attempts, including privilege-escalation attempts.</summary>
public sealed class UnauthorizedAccessRule : EventThresholdRule
{
    public override string Id => "unauthorized-access";
    public override string Name => "Unauthorized access attempts";
    public override string Description => "Repeated authentication failures or denied privileged operations by one identity.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 3, 300, 300);
    protected override string[] Categories => [EventCategories.AuthorizationDenied, EventCategories.Authentication];

    protected override bool Matches(SecurityEvent e) =>
        e.Category == EventCategories.AuthorizationDenied || e.Severity >= EventSeverity.Medium;

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = base.Evaluate(context, settings).ToList();
        foreach (var escalation in context.EventsWithin(settings, EventCategories.AuthorizationDenied)
            .Where(e => e.Message.Contains("requires Administrator", StringComparison.Ordinal))
            .GroupBy(e => e.Actor, StringComparer.Ordinal)
            .Where(g => findings.All(f => f.Key != g.Key)))
            findings.Add(new(escalation.Key, $"Privilege escalation attempt by {escalation.Key}",
                $"{escalation.Key} attempted {escalation.Count()} administrator-only operation(s).", null,
                EventEvidence(escalation.ToArray())));
        return findings;
    }

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Unauthorized access attempts by {group}",
            $"{events.Count} denied authentication or authorisation attempts by {group} within {settings.WindowSeconds}s.",
            null, EventEvidence(events));
}
