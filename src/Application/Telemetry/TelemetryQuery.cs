namespace Application.Telemetry;

public sealed record TelemetryQuery(string? TagId, DateTimeOffset? From, DateTimeOffset? To, int Limit = 500);
