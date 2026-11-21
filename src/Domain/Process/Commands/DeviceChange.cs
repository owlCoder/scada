namespace Domain.Process.Commands;

/// <summary>A state transition of an actuator, attributed to the path that caused it.</summary>
public sealed record DeviceChange(
    string DeviceId,
    string Property,
    string PreviousValue,
    string NewValue,
    ChangeCause Cause,
    string Actor);
