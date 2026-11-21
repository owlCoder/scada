using Domain.Control;

namespace Application.Simulation;

public sealed record ControllerStateRecord(string ControllerId, ControllerState State, IReadOnlyList<SetpointValue> Setpoints);
