using System.Globalization;
using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Many breakers opened within the rule window (<c>Threshold</c> openings in <c>WindowSeconds</c>, default 3 in 60 s),
/// regardless of the stated cause — the signature of a coordinated switching attack or a cascading failure.
/// </summary>
public sealed class GridMassSwitchingRule : GridDetectionRuleBase
{
    private readonly Lock _sync = new();
    private readonly Queue<(DateTimeOffset At, string SwitchId)> _openings = new();

    public override string Id => "grid-mass-switching";
    public override string Name => "Mass switching";
    public override string Description => "Many breakers opened within a short window.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 3, 60, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _openings.Count;
            _openings.Clear();
            return cleared;
        }
    }

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        lock (_sync)
        {
            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.SwitchOperated))
            {
                var nowOpen = context.Current.Switch(stepEvent.SourceId) is { Closed: false };
                if (nowOpen)
                    _openings.Enqueue((context.Now, stepEvent.SourceId));
            }

            foreach (var current in context.Current.Switches)
            {
                var before = context.Previous?.Switch(current.Id);
                if (before is { Closed: true } && !current.Closed)
                    _openings.Enqueue((context.Now, current.Id));
            }

            var windowStart = context.Now.AddSeconds(-settings.WindowSeconds);
            while (_openings.Count > 0 && _openings.Peek().At < windowStart)
                _openings.Dequeue();

            var threshold = Math.Max(1, (int)Math.Round(settings.Threshold));
            var switches = _openings.Select(o => o.SwitchId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (switches.Length < threshold)
                return [];

            return [new("mass-switching", $"Mass switching: {switches.Length} breakers opened in {settings.WindowSeconds}s",
                $"{switches.Length} switches opened within {settings.WindowSeconds}s: {string.Join(", ", switches)}.",
                null,
                [Evidence("switches", string.Join(",", switches),
                    string.Create(CultureInfo.InvariantCulture, $"{_openings.Count} opening(s) in window, threshold {threshold}"))])];
        }
    }
}
