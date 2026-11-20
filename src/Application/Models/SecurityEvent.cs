namespace Application;

public sealed record SecurityEvent(DateTimeOffset Timestamp, string Category, string Message, string Actor);
