namespace Protocols;

public sealed record RegisterDefinition(ushort Address, string Name, bool Writable, double Scale);
