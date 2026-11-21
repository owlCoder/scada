namespace Domain.Network;

public sealed record LinkDefinition(
    string Id,
    string Name,
    string SourceNode,
    string TargetNode,
    string SegmentId,
    double BaseLatencyMilliseconds,
    double PacketLossPercent,
    double TimeoutMilliseconds);
