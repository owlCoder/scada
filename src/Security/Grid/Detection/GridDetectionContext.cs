using Application.Detection;
using Application.Events;
using Application.Grid;
using Domain.Grid.Snapshots;

namespace Security.Grid.Detection;

/// <summary>Everything a grid rule may look at for one step: the report (snapshot + events), the previous snapshot and recent events.</summary>
public sealed class GridDetectionContext(
    GridStepReport report,
    GridSnapshot? previous,
    IReadOnlyList<SecurityEvent> recentEvents,
    DateTimeOffset now,
    double stepSeconds)
{
    public GridStepReport Report { get; } = report;
    public GridSnapshot Current => Report.Snapshot;
    public GridSnapshot? Previous { get; } = previous;
    public IReadOnlyList<SecurityEvent> RecentEvents { get; } = recentEvents;
    public DateTimeOffset Now { get; } = now;
    public double StepSeconds { get; } = stepSeconds > 0 ? stepSeconds : 1;

    /// <summary>Events (oldest first) within the rule window, measured in wall-clock time.</summary>
    public IReadOnlyList<SecurityEvent> EventsWithin(DetectionRuleSettings settings, params string[] categories) =>
        RecentEvents.Where(e => e.Timestamp >= Now.AddSeconds(-settings.WindowSeconds)
                && (categories.Length == 0 || categories.Contains(e.Category, StringComparer.Ordinal)))
            .ToArray();
}
