namespace Application.Telemetry;

/// <summary>One historian sample. <see cref="Hash"/> chains each sample to its predecessor for integrity checks.</summary>
public sealed record TelemetrySample(
    long Sequence,
    DateTimeOffset Timestamp,
    TimeSpan SimulationTime,
    string TagId,
    double Value,
    string Quality,
    string Hash);
