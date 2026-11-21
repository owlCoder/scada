namespace Domain.Network;

public sealed record NetworkSnapshot(
    IReadOnlyList<NetworkSegment> Segments,
    IReadOnlyList<LinkSnapshot> Links,
    IReadOnlyList<ConduitRule> AllowedConduits,
    long BlockedAttempts);
