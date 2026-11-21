using Domain.Control;

namespace Application.Registers;

public sealed record ControllerRegisterView(byte UnitId, string ControllerId, string Name, ControllerKind Kind, IReadOnlyList<RegisterSnapshot> Registers);
