using System.Globalization;
using Application.Detection;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Sustained branch loading above <c>Threshold</c> percent (default 100 %): the branch must stay above the threshold for
/// the whole rule window. Severity rises with the loading (High above 120 %, Critical above 150 %).
/// </summary>
public sealed class GridOverloadRule : GridDetectionRuleBase
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _overloads = new(StringComparer.Ordinal);

    public override string Id => "grid-overload";
    public override string Name => "Branch overload";
    public override string Description => "Sustained branch loading above 100 % of rating.";
    public override IncidentCategory Category => IncidentCategory.Operational;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Medium, 100, 60, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _overloads.Values.Sum(q => q.Count);
            _overloads.Clear();
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

            foreach (var branch in context.Current.Branches)
            {
                var overloaded = branch.Energized && branch.LoadingPercent > settings.Threshold;
                var queue = _overloads.TryGetValue(branch.Id, out var q) ? q : _overloads[branch.Id] = new();
                if (overloaded)
                    queue.Enqueue(context.Now);
                while (queue.Count > 0 && queue.Peek() < windowStart)
                    queue.Dequeue();

                if (queue.Count < sustainedSteps || !overloaded)
                    continue;

                var severity = branch.LoadingPercent > settings.Threshold + 50 ? DetectionSeverity.Critical
                    : branch.LoadingPercent > settings.Threshold + 20 ? DetectionSeverity.High
                    : (DetectionSeverity?)null;
                findings.Add(new(branch.Id, $"Sustained overload on {branch.Name}",
                    $"{branch.Id} ({branch.Name}) has been loaded at {branch.LoadingPercent:0.#}% for {settings.WindowSeconds}s " +
                    $"(threshold {settings.Threshold:0.#}%, current {branch.CurrentA:0.#} A).",
                    branch.Id,
                    [Evidence("branch", branch.Id, string.Create(CultureInfo.InvariantCulture,
                        $"{branch.LoadingPercent:0.#}% of rating, {branch.CurrentA:0.#} A"))],
                    severity));
            }

            return findings;
        }
    }
}
