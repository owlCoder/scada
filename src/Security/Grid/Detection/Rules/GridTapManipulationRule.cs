using System.Globalization;
using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Rapid or out-of-pattern tap changes on a transformer with an OLTC: at least <c>Threshold</c> tap changes within the
/// window, a forced (unattributed) tap change, or the tap driven to its limit while the LV bus voltage is outside its
/// configured limits.
/// </summary>
public sealed class GridTapManipulationRule(IGridView view) : GridDetectionRuleBase
{
    private readonly IGridView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<TimeSpan>> _changes = new(StringComparer.Ordinal);

    public override string Id => "grid-tap-manipulation";
    public override string Name => "Tap changer manipulation";
    public override string Description => "Rapid or out-of-pattern OLTC tap changes, or a tap driven to a limit causing voltage violations.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 3, 60, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _changes.Values.Sum(q => q.Count);
            _changes.Clear();
            return cleared;
        }
    }

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        lock (_sync)
        {
            var findings = new List<DetectionFinding>();
            // Tap timing is a property of the simulated process, so the window is measured in simulated time; with a time
            // scale above 1 a wall-clock window would count normal AVR operation over many simulated minutes as "rapid".
            var now = context.Current.SimulationTime;
            var windowStart = now - TimeSpan.FromSeconds(settings.WindowSeconds);

            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.TapChanged))
            {
                var queue = _changes.TryGetValue(stepEvent.SourceId, out var q) ? q : _changes[stepEvent.SourceId] = new();
                queue.Enqueue(now);
                if (stepEvent.Cause == GridChangeCause.Unattributed)
                    findings.Add(new($"{stepEvent.SourceId}:forced", $"Forced tap change on {stepEvent.SourceId}",
                        $"{stepEvent.Message} (cause {stepEvent.Cause}).", stepEvent.SourceId,
                        [Evidence("event", stepEvent.SourceId, stepEvent.Message)]));
            }

            var threshold = Math.Max(1, (int)Math.Round(settings.Threshold));
            foreach (var (transformerId, queue) in _changes)
            {
                while (queue.Count > 0 && queue.Peek() < windowStart)
                    queue.Dequeue();

                if (queue.Count >= threshold)
                    findings.Add(new($"{transformerId}:rapid", $"Rapid tap changes on {transformerId}",
                        $"{queue.Count} tap changes on {transformerId} within {settings.WindowSeconds}s (threshold {threshold}).",
                        transformerId,
                        [Evidence("tap", transformerId,
                            string.Create(CultureInfo.InvariantCulture, $"{queue.Count} changes in {settings.WindowSeconds}s"))]));
            }

            foreach (var definition in _view.Definition.Transformers.Where(t => t.HasOnLoadTapChanger))
            {
                var branch = context.Current.Branch(definition.Id);
                if (branch?.TapPosition is not { } tap)
                    continue;
                if (tap > definition.TapMin && tap < definition.TapMax)
                    continue;

                var lvBus = context.Current.Bus(definition.LvBusId);
                var lvDefinition = _view.Definition.Buses.FirstOrDefault(b => b.Id == definition.LvBusId);
                var violation = lvBus is not null && lvDefinition is not null
                    && (lvBus.VoltagePu < lvDefinition.MinVoltagePu || lvBus.VoltagePu > lvDefinition.MaxVoltagePu);
                findings.Add(new($"{definition.Id}:limit", $"Tap of {definition.Id} driven to its limit ({tap})",
                    $"Tap position {tap} is at the limit of {definition.TapMin}..{definition.TapMax} on {definition.Name}" +
                    (violation
                        ? $" and LV bus {lvBus!.Id} voltage is {lvBus.VoltagePu:0.###} pu, outside {lvDefinition!.MinVoltagePu:0.##}..{lvDefinition.MaxVoltagePu:0.##}."
                        : "."),
                    definition.Id,
                    [Evidence("tap", definition.Id,
                        string.Create(CultureInfo.InvariantCulture, $"tap {tap}, limits {definition.TapMin}..{definition.TapMax}"))]));
            }

            return findings;
        }
    }
}
