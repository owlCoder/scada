using Application.Detection;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class DetectionRuleSettingsEntity
{
    public string RuleId { get; set; } = "";
    public bool Enabled { get; set; }
    public DetectionSeverity Severity { get; set; }
    public double Threshold { get; set; }
    public int WindowSeconds { get; set; }
    public int CooldownSeconds { get; set; }

    public DetectionRuleSettingsEntity Apply(DetectionRuleSettings s)
    {
        RuleId = s.RuleId;
        Enabled = s.Enabled;
        Severity = s.Severity;
        Threshold = s.Threshold;
        WindowSeconds = s.WindowSeconds;
        CooldownSeconds = s.CooldownSeconds;
        return this;
    }

    public DetectionRuleSettings ToRecord() => new(RuleId, Enabled, Severity, Threshold, WindowSeconds, CooldownSeconds);
}
