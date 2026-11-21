namespace Api.Contracts;

/// <summary>Either <see cref="Position"/> (percent) or the legacy <see cref="Open"/> flag (100 % / 0 %).</summary>
public sealed record ValveCommandRequest(double? Position = null, bool? Open = null, long? SequenceNumber = null, string? Actor = null);
