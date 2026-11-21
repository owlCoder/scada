namespace Domain.Network;

public sealed record BlockedConnectionAttempt(string SourceNode, NetworkZone SourceZone, string TargetNode, NetworkZone TargetZone, string Reason);
