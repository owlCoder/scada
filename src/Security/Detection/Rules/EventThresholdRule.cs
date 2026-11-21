using Application.Detection;
using Application.Events;

namespace Security.Detection.Rules;

/// <summary>Counts matching events per group inside the rule window and fires when the count reaches the threshold.</summary>
public abstract class EventThresholdRule : DetectionRuleBase
{
    protected abstract string[] Categories { get; }

    protected virtual bool Matches(SecurityEvent securityEvent) => true;

    protected virtual string GroupKey(SecurityEvent securityEvent) => securityEvent.Actor;

    protected abstract DetectionFinding CreateFinding(string group, IReadOnlyList<SecurityEvent> events, DetectionRuleSettings settings);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings) =>
        context.EventsWithin(settings, Categories)
            .Where(Matches)
            .GroupBy(GroupKey, StringComparer.Ordinal)
            .Where(g => g.Count() >= Math.Max(1, settings.Threshold))
            .Select(g => CreateFinding(g.Key, g.ToArray(), settings))
            .ToArray();

    protected static IReadOnlyList<DetectionEvidence> EventEvidence(IReadOnlyList<SecurityEvent> events) =>
        events.TakeLast(5).Select(e => new DetectionEvidence("event", e.Id.ToString("N"), $"{e.Category}: {e.Message}")).ToArray();
}
