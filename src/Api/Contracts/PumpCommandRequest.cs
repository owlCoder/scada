namespace Api.Contracts;

/// <summary><see cref="Actor"/> is accepted for backwards compatibility and ignored; the verified identity is used.</summary>
public sealed record PumpCommandRequest(bool Running, long? SequenceNumber = null, string? Actor = null);
