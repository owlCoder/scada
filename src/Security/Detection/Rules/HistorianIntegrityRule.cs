using Application.Detection;
using Application.Telemetry;
using Domain.Incidents;

namespace Security.Detection.Rules;

/// <summary>
/// Verifies the historian hash chain every <c>Threshold</c> steps. Each altered sample is reported exactly once, so a
/// historian that stays corrupted does not keep raising detections in later scenario runs.
/// </summary>
public sealed class HistorianIntegrityRule(TelemetryHistorian historian) : DetectionRuleBase
{
    private const int MaxRemembered = 10_000;
    private readonly HashSet<long> _reported = [];
    private readonly Queue<long> _reportedOrder = new();
    private long _evaluations;

    public override string Id => "historian-integrity";
    public override string Name => "Historian data integrity violation";
    public override string Description => "A historian sample no longer matches its hash chain.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Critical, 4, 60, 600);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        if (_evaluations++ % Math.Max(1, (long)settings.Threshold) != 0)
            return [];

        var findings = new List<DetectionFinding>();
        foreach (var violation in historian.FindViolations().Where(v => _reported.Add(v.Sequence)))
        {
            _reportedOrder.Enqueue(violation.Sequence);
            if (_reportedOrder.Count > MaxRemembered)
                _reported.Remove(_reportedOrder.Dequeue());
            findings.Add(new DetectionFinding($"sequence:{violation.Sequence}", $"Historian record for {violation.TagId} was altered",
                $"Sample {violation.Sequence} ({violation.TagId}, recorded {violation.Timestamp:O}) fails hash-chain verification.",
                violation.TagId, [Evidence("historian", $"{violation.Sequence}", $"Tag {violation.TagId}")]));
        }

        return findings;
    }
}
