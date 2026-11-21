using Application.Detection;
using Domain.Incidents;

namespace Security.Detection.Rules;

public abstract class DetectionRuleBase : IDetectionRule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract IncidentCategory Category { get; }
    public abstract DetectionRuleSettings DefaultSettings { get; }

    public abstract IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings);

    public virtual int ResetState() => 0;

    protected DetectionRuleSettings Settings(DetectionSeverity severity, double threshold, int windowSeconds, int cooldownSeconds) =>
        new(Id, true, severity, threshold, windowSeconds, cooldownSeconds);

    protected static DetectionEvidence Evidence(string kind, string reference, string detail) => new(kind, reference, detail);
}
