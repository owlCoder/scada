namespace Application.Registers;

public sealed record RegisterReadResult(RegisterOperationStatus Status, IReadOnlyList<ushort> Values, string Message);
