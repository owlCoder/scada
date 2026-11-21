namespace Domain.Process.Safety;

/// <summary>A condition that the safety system would have acted on, recorded while interlocks are bypassed.</summary>
public sealed record SafetyViolation(string DeviceId, string Description);
