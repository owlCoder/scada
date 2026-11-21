using Application.Commands;
using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

public sealed class ExcessiveControlRequestsRule : EventThresholdRule
{
    private static readonly HashSet<string> CommandSources =
        Enum.GetNames<CommandSource>().ToHashSet(StringComparer.Ordinal);

    public override string Id => "excessive-control-requests";
    public override string Name => "Excessive control requests";
    public override string Description => "Unusually high control command frequency from a single identity.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 8, 60, 300);
    protected override string[] Categories => [EventCategories.Audit, EventCategories.CommandRejected, EventCategories.Communication, EventCategories.SecurityAlert];

    protected override bool Matches(SecurityEvent e) =>
        (CommandSources.Contains(e.Source) && e.DeviceId is not null) ||
        (e.Category == EventCategories.SecurityAlert && e.Message.StartsWith("Too many commands", StringComparison.Ordinal));

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = base.Evaluate(context, settings).ToList();
        foreach (var limited in context.EventsWithin(settings, EventCategories.SecurityAlert)
            .Where(e => e.Message.StartsWith("Too many commands", StringComparison.Ordinal))
            .GroupBy(e => e.Actor, StringComparer.Ordinal)
            .Where(g => findings.All(f => f.Key != g.Key)))
            findings.Add(CreateFinding(limited.Key, limited.ToArray(), settings));
        return findings;
    }

    protected override DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings) =>
        new(group, $"Excessive control requests from {group}",
            $"{events.Count} control requests or rate-limit alerts for {group} within {settings.WindowSeconds}s.",
            events.LastOrDefault(e => e.DeviceId is not null)?.DeviceId, EventEvidence(events));
}
