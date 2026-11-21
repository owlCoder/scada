namespace Domain.Control;

public sealed record ValveControlLoop(
    string Name,
    string LevelSensorId,
    string ValveId,
    ushort OpenBelowRegister,
    ushort CloseAboveRegister,
    double DefaultOpenBelow,
    double DefaultCloseAbove);
