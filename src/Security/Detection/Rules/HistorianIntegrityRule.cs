using Application.Detection;
using Application.Telemetry;
using Domain.Incidents;

namespace Security.Detection.Rules;

/// <summary>Verifies the historian hash chain every <c>Threshold</c> steps.</summary>
public sealed class HistorianIntegrityRule(TelemetryHistorian historian) : DetectionRuleBase
{
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

        var report = historian.Verify();
        return report.IsValid
            ? []
            : [new DetectionFinding($"sequence:{report.FirstInvalidSequence}", $"Historian record for {report.TagId} was altered",
                $"Sample {report.FirstInvalidSequence} ({report.TagId}) fails hash-chain verification among {report.SamplesChecked} samples.",
                report.TagId, [Evidence("historian", $"{report.FirstInvalidSequence}", $"Tag {report.TagId}")])];
    }
}
