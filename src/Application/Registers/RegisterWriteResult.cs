namespace Application.Registers;

public sealed record RegisterWriteResult(RegisterOperationStatus Status, string Message);
