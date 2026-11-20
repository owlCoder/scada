namespace Application;

public sealed record TelemetrySample(DateTimeOffset Timestamp, double TankLevel, bool PumpRunning, long Revision);
