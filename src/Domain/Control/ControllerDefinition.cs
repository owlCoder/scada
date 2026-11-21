namespace Domain.Control;

public sealed record ControllerDefinition(
    string Id,
    string Name,
    ControllerKind Kind,
    int ScanCycleMilliseconds,
    string FieldLinkId,
    string SupervisoryLinkId,
    int TimeoutThreshold,
    IReadOnlyList<IoPoint> Inputs,
    IReadOnlyList<IoPoint> Outputs,
    IReadOnlyList<PumpControlLoop> PumpLoops,
    IReadOnlyList<ValveControlLoop> ValveLoops)
{
    public IReadOnlyList<string> ActuatorIds => Outputs.Select(o => o.DeviceId).Distinct(StringComparer.Ordinal).ToArray();
}
