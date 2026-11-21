namespace Domain.Network;

public sealed record LinkSnapshot(
    string Id,
    string Name,
    string SourceNode,
    string TargetNode,
    string SegmentId,
    LinkState State,
    double LatencyMilliseconds,
    double PacketLossPercent,
    double TimeoutMilliseconds,
    long Sent,
    long Delivered,
    long Lost,
    long TimedOut,
    double AverageLatencyMilliseconds,
    double HealthPercent);
