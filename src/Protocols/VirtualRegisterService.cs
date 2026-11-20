using Application;

namespace Protocols;

public sealed record RegisterDefinition(ushort Address, string Name, bool Writable, double Scale);

public sealed record RegisterValue(ushort Address, string Name, ushort Value, bool Writable);
