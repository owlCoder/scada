using Application.Detection;
using Domain.Incidents;
using Domain.Process.Devices;

namespace Security.Detection.Rules;

public sealed class SafetyViolationRule : DetectionRuleBase
{
    public override string Id => "process-safety-violation";
    public override string Name => "Process safety violation";
    public override string Description => "Safety conditions breached while interlocks are bypassed.";
    public override IncidentCategory Category => IncidentCategory.ProcessSafety;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Critical, 0, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = context.Report.Plant.SafetyViolations
            .Select(v => new DetectionFinding(v.DeviceId, $"Safety violation on {v.DeviceId}", v.Description, v.DeviceId,
                [Evidence("safety-system", v.DeviceId, v.Description)]))
            .ToList();

        if (context.Current.InterlocksBypassed)
        {
            foreach (var tank in context.Current.Tanks)
            {
                var level = context.Current.Sensors.FirstOrDefault(s => s.Kind == SensorKind.Level && s.DeviceId == tank.Id)?.ReportedValue
                    ?? tank.LevelPercent;
                if (level >= tank.Thresholds.High && findings.All(f => f.Key != tank.Id))
                    findings.Add(new(tank.Id, $"{tank.Id} above high limit with interlocks bypassed",
                        $"{tank.Name} at {level:0.0}% while the safety instrumented system is bypassed.", tank.Id,
                        [Evidence("safety-system", "SIS", "Interlocks bypassed")]));
            }
        }

        return findings;
    }
}
