namespace Application.Telemetry;

public sealed record TelemetryPoint(DateTimeOffset Timestamp, TimeSpan SimulationTime, double Value, string Quality);
