namespace Api.Contracts;

public sealed record LinkImpairmentRequest(double LatencyMilliseconds, double PacketLossPercent);
