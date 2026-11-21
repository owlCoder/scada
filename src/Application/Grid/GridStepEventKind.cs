namespace Application.Grid;

public enum GridStepEventKind
{
    SwitchOperated, ProtectionPickup, ProtectionTripped, TapChanged, CapacitorChanged, LoadShed, LoadRestored,
    SupplyLost, SupplyRestored, AlarmRaised, AlarmCleared, CommandAccepted, CommandRejected, ProtectionSettingChanged,
    MeasurementSpoofed, FaultInjected, FaultCleared, RtuOnlineChanged, DefinitionActivated
}
