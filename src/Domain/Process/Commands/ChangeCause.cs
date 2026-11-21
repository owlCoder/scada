namespace Domain.Process.Commands;

/// <summary>
/// Records why an actuator changed. <see cref="Unattributed"/> marks a change that bypassed every
/// legitimate control path (used by training scenarios to model tampering).
/// </summary>
public enum ChangeCause { None, Operator, Controller, Interlock, FailSafe, Unattributed }
