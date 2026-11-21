namespace Domain.Process.Hydraulics;

public sealed record HydraulicResult(
    IReadOnlyDictionary<string, double> DeviceFlows,
    IReadOnlyDictionary<string, double> PumpPressures,
    IReadOnlyDictionary<string, double> TankOverflow);
