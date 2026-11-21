using Application.Detection;

namespace Api.Contracts;

public sealed record RuleSettingsRequest(bool Enabled, DetectionSeverity Severity, double Threshold, int WindowSeconds, int CooldownSeconds);
