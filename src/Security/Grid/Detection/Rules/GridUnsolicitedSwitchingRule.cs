using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// A switch changed state with cause <see cref="GridChangeCause.Unattributed"/>, or with cause Operator but without a
/// matching accepted operator command — Industroyer-style direct operation of breakers by a compromised client.
/// Protection and automatic causes are legitimate.
/// </summary>
public sealed class GridUnsolicitedSwitchingRule : GridDetectionRuleBase
{
    public override string Id => "grid-unsolicited-switching";
    public override string Name => "Unsolicited switching";
    public override string Description => "A switch changed state without an accepted operator command or a protection trip.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Critical, 0, 60, 60);

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        var candidates = new Dictionary<string, GridStepEvent?>(StringComparer.Ordinal);

        foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.SwitchOperated))
            candidates[stepEvent.SourceId] = stepEvent;

        foreach (var current in context.Current.Switches)
        {
            var before = context.Previous?.Switch(current.Id);
            if (before is not null && before.Closed != current.Closed)
                candidates.TryAdd(current.Id, null);
        }

        foreach (var (switchId, stepEvent) in candidates)
        {
            var snapshot = context.Current.Switch(switchId);
            var cause = stepEvent?.Cause ?? snapshot?.LastCause ?? GridChangeCause.Unattributed;
            var actor = stepEvent?.Actor ?? snapshot?.LastOperatedBy;
            if (cause is GridChangeCause.Protection or GridChangeCause.Automatic or GridChangeCause.System)
                continue;

            if (cause == GridChangeCause.Operator && HasAcceptedCommand(context, switchId, actor, settings))
                continue;

            var state = snapshot is null ? "unknown" : snapshot.Closed ? "closed" : "open";
            findings.Add(new(switchId, $"Unsolicited operation of {switchId}",
                $"{snapshot?.Name ?? switchId} changed state to {state} with cause {cause}" +
                (actor is null ? " and no operator." : $" by {actor} without an accepted command."),
                switchId,
                [Evidence("switch", switchId, $"Cause {cause}, operated by {actor ?? "nobody"}, now {state}"),
                    Evidence("event", stepEvent?.SourceId ?? switchId, stepEvent?.Message ?? "state changed without a step event")]));
        }

        return findings;
    }

    private static bool HasAcceptedCommand(GridDetectionContext context, string switchId, string? actor, DetectionRuleSettings settings)
    {
        // Report events belong to the current step, so they are always inside the window.
        _ = settings;
        return context.Report.Events.Any(e => e.Kind == GridStepEventKind.CommandAccepted
            && e.SourceId == switchId
            && (actor is null || e.Actor is null || string.Equals(e.Actor, actor, StringComparison.Ordinal)));
    }
}
