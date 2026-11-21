namespace Application.Telemetry;

public sealed record IntegrityReport(bool IsValid, int SamplesChecked, long? FirstInvalidSequence, string? TagId);
