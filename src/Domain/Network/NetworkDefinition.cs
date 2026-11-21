namespace Domain.Network;

public sealed record NetworkDefinition(
    IReadOnlyList<NetworkSegment> Segments,
    IReadOnlyList<LinkDefinition> Links,
    IReadOnlyList<ConduitRule> AllowedConduits);
