namespace Api.Contracts;

public sealed record PumpCommand(bool Running, string? Actor = null);
