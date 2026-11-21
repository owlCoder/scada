using System.Globalization;
using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Sustained voltage outside the bus limits from the definition: a bus must stay outside its
/// <c>MinVoltagePu</c>/<c>MaxVoltagePu</c> range for the whole rule window before the rule fires. The severity rises
/// with the deviation (High at 0.05 pu beyond the limit, Critical at 0.10 pu).
/// </summary>
public sealed class GridVoltageViolationRule(IGridView view) : GridDetectionRuleBase
{
    private readonly IGridView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _violations = new(StringComparer.Ordinal);

    public override string Id => "grid-voltage-violation";
    public override string Name => "Voltage violation";
    public override string Description => "Sustained voltage outside the configured bus limits.";
    public override IncidentCategory Category => IncidentCategory.Operational;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 0, 60, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _violations.Values.Sum(q => q.Count);
            _violations.Clear();
            return cleared;
        }
    }

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        lock (_sync)
        {
            var findings = new List<DetectionFinding>();
            var windowStart = context.Now.AddSeconds(-settings.WindowSeconds);
            var sustainedSteps = Math.Max(1, (int)Math.Floor(settings.WindowSeconds / context.StepSeconds));

            foreach (var definition in _view.Definition.Buses)
            {
                var bus = context.Current.Bus(definition.Id);
                if (bus is null || !bus.Energized)
                    continue;

                var violating = bus.VoltagePu < definition.MinVoltagePu || bus.VoltagePu > definition.MaxVoltagePu;
                var queue = _violations.TryGetValue(bus.Id, out var q) ? q : _violations[bus.Id] = new();
                if (violating)
                    queue.Enqueue(context.Now);
                while (queue.Count > 0 && queue.Peek() < windowStart)
                    queue.Dequeue();

                // Sustained: the bus has been outside the limits for the whole window.
                if (queue.Count < sustainedSteps || !violating)
                    continue;

                var deviation = Math.Max(definition.MinVoltagePu - bus.VoltagePu, bus.VoltagePu - definition.MaxVoltagePu);
                var severity = deviation >= 0.10 ? DetectionSeverity.Critical
                    : deviation >= 0.05 ? DetectionSeverity.High
                    : (DetectionSeverity?)null;
                findings.Add(new(bus.Id, $"Sustained voltage violation at {bus.Name}",
                    $"{bus.Id} has been at {bus.VoltagePu:0.###} pu for {settings.WindowSeconds}s " +
                    $"(limits {definition.MinVoltagePu:0.##}..{definition.MaxVoltagePu:0.##}, deviation {deviation:0.###} pu).",
                    bus.Id,
                    [Evidence("bus", bus.Id, string.Create(CultureInfo.InvariantCulture,
                        $"{bus.VoltageKv:0.###} kV = {bus.VoltagePu:0.###} pu, limits {definition.MinVoltagePu:0.##}..{definition.MaxVoltagePu:0.##}"))],
                    severity));
            }

            return findings;
        }
    }
}
