namespace Domain.Control;

public sealed record ControllerScanResult(
    string ControllerId,
    bool Executed,
    IReadOnlyList<ActuatorCommand> Commands,
    int CommandsDelivered,
    bool CommunicationLost,
    bool CommunicationRestored,
    bool EnteredSafeState,
    IReadOnlyList<string> FailSafeDevices);
