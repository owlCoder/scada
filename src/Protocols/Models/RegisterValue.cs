namespace Protocols;

public sealed record RegisterValue(ushort Address, string Name, ushort Value, bool Writable);
