namespace Domain.Process.Configuration;

/// <summary>A centrifugal pump that moves water from a source tank to a target tank through a discharge valve.</summary>
public sealed record PumpDefinition(
    string Id,
    string Name,
    string SourceTankId,
    string TargetTankId,
    string DischargeValveId,
    double NominalFlowRate,
    double ShutoffPressureBar,
    double RampSeconds);
