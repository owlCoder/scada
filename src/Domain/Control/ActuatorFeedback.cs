using Domain.Process.Devices;

namespace Domain.Control;

/// <summary>Commanded state of an actuator as read back from the field. Unavailable means faulted or interlocked.</summary>
public sealed record ActuatorFeedback(string DeviceId, double Value, ControlMode Mode, bool IsAvailable);
