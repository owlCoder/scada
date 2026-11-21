using Domain.Process.Devices;

namespace Domain.Process.Commands;

public sealed record FaultInjection(
    string DeviceId,
    PumpFault PumpFault = PumpFault.None,
    ValveFault ValveFault = ValveFault.None,
    SensorFault SensorFault = SensorFault.None,
    double DriftOffset = 0);
