using System.Globalization;
using Application.Detection;
using Domain.Incidents;
using Domain.Process.Devices;

namespace Security.Detection.Rules;

public sealed class AbnormalTankLevelRule : DetectionRuleBase
{
    public override string Id => "abnormal-tank-level";
    public override string Name => "Abnormal tank level";
    public override string Description => "Reported level beyond the high-high or low-low threshold, or a tank overflow switch.";
    public override IncidentCategory Category => IncidentCategory.ProcessSafety;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 0, 60, 120);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        foreach (var tank in context.Current.Tanks)
        {
            var sensor = context.Current.Sensors.FirstOrDefault(s => s.Kind == SensorKind.Level && s.DeviceId == tank.Id);
            var level = sensor?.ReportedValue ?? tank.LevelPercent;
            var text = level.ToString("0.0", CultureInfo.InvariantCulture);
            if (tank.IsOverflowing)
                findings.Add(new(tank.Id, $"{tank.Name} overflow",
                    $"The overflow switch of {tank.Id} is active (reported level {text}%).", tank.Id,
                    [Evidence("tank", tank.Id, $"Overflow total {tank.TotalOverflowVolume:0.###} m3")], DetectionSeverity.Critical));
            else if (level >= tank.Thresholds.HighHigh)
                findings.Add(new(tank.Id, $"{tank.Name} level high-high",
                    $"{tank.Id} reports {text}% (high-high {tank.Thresholds.HighHigh}%).", tank.Id,
                    [Evidence("measurement", sensor?.Id ?? tank.Id, $"{text}%")]));
            else if (level <= tank.Thresholds.LowLow)
                findings.Add(new(tank.Id, $"{tank.Name} level low-low",
                    $"{tank.Id} reports {text}% (low-low {tank.Thresholds.LowLow}%).", tank.Id,
                    [Evidence("measurement", sensor?.Id ?? tank.Id, $"{text}%")]));
        }

        return findings;
    }
}
