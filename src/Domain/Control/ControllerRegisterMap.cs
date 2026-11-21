namespace Domain.Control;

/// <summary>
/// Holding register layout shared by all controllers: outputs from 0, loop setpoints from 100 and
/// controller status from 200. Input registers carry measurements from address 0.
/// </summary>
public static class ControllerRegisterMap
{
    public const int Size = 256;
    public const ushort SetpointBase = 100;
    public const ushort StatusBase = 200;
    public const ushort ControllerState = 200;
    public const ushort FieldCommunication = 201;
    public const ushort ScanCounter = 202;
    public const ushort ConsecutiveFailures = 203;
    public const ushort LoopDemandBase = 210;
}
