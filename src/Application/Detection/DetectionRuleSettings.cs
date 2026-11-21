namespace Application.Detection;

/// <summary>Runtime configuration of a detection rule. Threshold semantics are rule-specific.</summary>
public sealed record DetectionRuleSettings(
    string RuleId,
    bool Enabled,
    DetectionSeverity Severity,
    double Threshold,
    int WindowSeconds,
    int CooldownSeconds);
