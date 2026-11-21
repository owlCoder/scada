using Application.Detection;
using Domain.Incidents;

namespace Security.Detection;

public interface IDetectionRule
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    IncidentCategory Category { get; }
    DetectionRuleSettings DefaultSettings { get; }

    IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings);
}
