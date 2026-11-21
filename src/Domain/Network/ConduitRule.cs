namespace Domain.Network;

/// <summary>An allowed communication path between two zones. Anything not listed is denied.</summary>
public sealed record ConduitRule(NetworkZone From, NetworkZone To);
