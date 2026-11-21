namespace Domain.Grid.Commands;

/// <summary>
/// Supervisory commands. Switches use select-before-operate: <see cref="SelectOpen"/> or <see cref="SelectClose"/>, then
/// <see cref="Execute"/> within the selection timeout, or <see cref="Cancel"/>.
/// </summary>
public enum GridCommandType
{
    SelectOpen, SelectClose, Execute, Cancel,
    TapRaise, TapLower, SetTapAutomatic, SetTapManual,
    SetCapacitorSteps, ShedLoad, RestoreLoad, ResetProtection, AcknowledgeAlarm
}
