using Application.Commands;
using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

/// <summary>Mass alarm acknowledgement or bulk manual overrides by a single operator.</summary>
public sealed class OperatorMisuseRule : DetectionRuleBase
{
    public override string Id => "operator-misuse";
    public override string Name => "Operator misuse";
    public override string Description => "Mass alarm acknowledgement or switching many devices to manual in a short time.";
    public override IncidentCategory Category => IncidentCategory.Operational;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 4, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var audit = context.EventsWithin(settings, EventCategories.Audit);
        var findings = new List<DetectionFinding>();
        foreach (var group in audit.Where(e => e.Message.StartsWith("Alarm ", StringComparison.Ordinal) && e.Message.EndsWith(" acknowledged", StringComparison.Ordinal))
            .GroupBy(e => e.Actor, StringComparer.Ordinal).Where(g => g.Count() >= settings.Threshold))
            findings.Add(new($"{group.Key}:ack", $"Mass alarm acknowledgement by {group.Key}",
                $"{group.Key} acknowledged {group.Count()} alarms within {settings.WindowSeconds}s.", null,
                group.TakeLast(5).Select(e => Evidence("event", e.Id.ToString("N"), e.Message)).ToArray()));

        foreach (var group in audit.Where(e => e.Message.StartsWith(nameof(OperatorCommandType.SetManual), StringComparison.Ordinal))
            .GroupBy(e => e.Actor, StringComparer.Ordinal)
            .Where(g => g.Select(e => e.DeviceId).Distinct(StringComparer.Ordinal).Count() >= Math.Max(2, settings.Threshold - 1)))
            findings.Add(new($"{group.Key}:manual", $"Bulk manual override by {group.Key}",
                $"{group.Key} switched {group.Select(e => e.DeviceId).Distinct(StringComparer.Ordinal).Count()} devices to manual within {settings.WindowSeconds}s.",
                null, group.TakeLast(5).Select(e => Evidence("event", e.Id.ToString("N"), e.Message)).ToArray()));
        return findings;
    }
}
