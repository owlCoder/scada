using Application.Detection;
using Application.Events;
using Application.Simulation;
using Domain.Process.Snapshots;

namespace Security.Detection;

public sealed class DetectionContext(
    SimulationStepReport report,
    PlantSnapshot? previous,
    IReadOnlyList<SecurityEvent> recentEvents,
    DateTimeOffset now,
    double stepSeconds)
{
    public SimulationStepReport Report { get; } = report;
    public PlantSnapshot Current => Report.Plant.Snapshot;
    public PlantSnapshot? Previous { get; } = previous;
    public DateTimeOffset Now { get; } = now;
    public double StepSeconds { get; } = stepSeconds;

    /// <summary>Events (oldest first) within the rule window, measured in wall-clock time.</summary>
    public IReadOnlyList<SecurityEvent> EventsWithin(DetectionRuleSettings settings, params string[] categories) =>
        recentEvents.Where(e => e.Timestamp >= Now.AddSeconds(-settings.WindowSeconds)
                && (categories.Length == 0 || categories.Contains(e.Category, StringComparer.Ordinal)))
            .ToArray();
}
