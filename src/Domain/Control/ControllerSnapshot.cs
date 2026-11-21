namespace Domain.Control;

public sealed record ControllerSnapshot(
    string Id,
    string Name,
    ControllerKind Kind,
    ControllerState State,
    CommunicationState FieldCommunication,
    CommunicationState SupervisoryCommunication,
    int ScanCycleMilliseconds,
    long ScanCount,
    int ConsecutiveFailures,
    long TotalFailures,
    long CommandsIssued,
    TimeSpan LastScanAt,
    string? LastError,
    IReadOnlyList<string> ActuatorIds,
    IReadOnlyList<string> SensorIds,
    IReadOnlyList<RegisterSnapshot> Registers,
    int ForcedRegisterCount);
