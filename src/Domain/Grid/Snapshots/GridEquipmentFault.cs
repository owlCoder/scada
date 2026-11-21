namespace Domain.Grid.Snapshots;

/// <summary>Faults injected by the instructor or produced by the scenario engine. Hidden from operators.</summary>
public enum GridEquipmentFault { None, ThreePhaseShortCircuit, EarthFault, SwitchStuck, Overheating }
