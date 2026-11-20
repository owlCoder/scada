namespace Api.Contracts;

public sealed record RegisterWrite(ushort Value, string? Actor = null);
