namespace Domain.Control;

public sealed record RegisterSnapshot(RegisterArea Area, ushort Address, string Tag, ushort Value, bool IsForced);
