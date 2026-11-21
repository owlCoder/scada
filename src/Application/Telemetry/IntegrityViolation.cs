namespace Application.Telemetry;

/// <summary>A historian sample whose stored hash no longer matches its contents.</summary>
public sealed record IntegrityViolation(long Sequence, string TagId, DateTimeOffset Timestamp);
