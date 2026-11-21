using Domain.Process.Commands;

namespace Application.Simulation;

public sealed record ControllerCommandRecord(string ControllerId, string DeviceId, double Value, CommandResult Result);
