namespace Application.Registers;

/// <summary>
/// Register writes are disabled unless explicitly enabled for an isolated training environment.
/// Only loop setpoints (holding registers 100..199) are ever writable.
/// </summary>
public sealed class RegisterWriteOptions
{
    public bool WritesEnabled { get; set; }
    public ushort MaxSetpointValue { get; set; } = 1000;
}
