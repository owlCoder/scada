namespace Domain.Grid.Commands;

/// <summary><see cref="Value"/> is used by <see cref="GridCommandType.SetCapacitorSteps"/>; <see cref="SequenceNumber"/> feeds the replay guard.</summary>
public sealed record GridCommand(GridCommandType Type, string TargetId, double? Value = null, long? SequenceNumber = null);
