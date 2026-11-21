using System.Globalization;
using Application.Commands;
using Application.Detection;
using Application.Events;
using Domain.Incidents;

namespace Security.Detection.Rules;

/// <summary>
/// Detects opposing commands to one device from different operators, and rapid command oscillation by one
/// operator (<c>Threshold</c> direction changes within the window).
/// </summary>
public sealed class ConflictingOperatorCommandsRule : DetectionRuleBase
{
    public override string Id => "conflicting-operator-commands";
    public override string Name => "Conflicting operator commands";
    public override string Description => "Opposing commands to the same device, or rapid oscillating commands.";
    public override IncidentCategory Category => IncidentCategory.Operational;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 4, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var commands = context.EventsWithin(settings, EventCategories.Audit)
            .Where(e => e.DeviceId is not null)
            .Select(e => (Event: e, Value: Parse(e.Message)))
            .Where(x => x.Value is not null)
            .GroupBy(x => x.Event.DeviceId!, StringComparer.Ordinal);

        var findings = new List<DetectionFinding>();
        foreach (var device in commands)
        {
            var items = device.ToArray();
            var conflicting = items.GroupBy(x => x.Value).Select(g => g.Select(x => x.Event.Actor).ToHashSet(StringComparer.Ordinal)).ToArray();
            if (conflicting.Length > 1 && conflicting.SelectMany(a => a).Distinct(StringComparer.Ordinal).Count() > 1 &&
                conflicting.Any(a => conflicting.Any(b => !a.SetEquals(b))))
            {
                findings.Add(new($"{device.Key}:conflict", $"Conflicting commands to {device.Key}",
                    $"Different operators issued opposing commands to {device.Key} within {settings.WindowSeconds}s.", device.Key,
                    items.TakeLast(5).Select(x => Evidence("event", x.Event.Id.ToString("N"), $"{x.Event.Actor}: {x.Event.Message}")).ToArray()));
                continue;
            }

            foreach (var actor in items.GroupBy(x => x.Event.Actor, StringComparer.Ordinal))
            {
                var values = actor.Select(x => x.Value!.Value).ToArray();
                var changes = values.Zip(values.Skip(1)).Count(p => Math.Abs(p.First - p.Second) > 0.01);
                if (changes >= settings.Threshold)
                    findings.Add(new($"{device.Key}:{actor.Key}:oscillation", $"Oscillating commands to {device.Key}",
                        string.Create(CultureInfo.InvariantCulture, $"{actor.Key} reversed the command to {device.Key} {changes} times within {settings.WindowSeconds}s."),
                        device.Key, actor.TakeLast(5).Select(x => Evidence("event", x.Event.Id.ToString("N"), x.Event.Message)).ToArray()));
            }
        }

        return findings;
    }

    /// <summary>Extracts the commanded value from an audit message: 1/0 for pump start/stop, percent for valves.</summary>
    private static double? Parse(string message)
    {
        if (message.StartsWith(nameof(OperatorCommandType.StartPump), StringComparison.Ordinal))
            return 1;
        if (message.StartsWith(nameof(OperatorCommandType.StopPump), StringComparison.Ordinal))
            return 0;
        if (!message.StartsWith("Set ", StringComparison.Ordinal))
            return null;

        var to = message.IndexOf(" to ", StringComparison.Ordinal);
        var percent = message.IndexOf('%', StringComparison.Ordinal);
        return to > 0 && percent > to &&
            double.TryParse(message.AsSpan(to + 4, percent - to - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
