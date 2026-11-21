using Application.Detection;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection;

/// <summary>
/// One grid detection rule, evaluated by <see cref="GridDetectionEngine"/> after every grid step over the step report
/// (snapshot + events) and the recent security events.
/// </summary>
public interface IGridDetectionRule
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    IncidentCategory Category { get; }
    DetectionRuleSettings DefaultSettings { get; }

    IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings);

    /// <summary>Clears accumulated evaluation state when a new detection scope (scenario run) starts.</summary>
    int ResetState();
}
