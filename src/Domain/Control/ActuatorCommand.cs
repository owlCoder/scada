namespace Domain.Control;

/// <summary>Pump commands use 1 (run) or 0 (stop); valve commands use the opening in percent.</summary>
public sealed record ActuatorCommand(string DeviceId, double Value);
