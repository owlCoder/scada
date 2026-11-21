namespace Domain.Grid.Definition;

/// <summary>ANSI device numbers: 50 instantaneous and 51 time overcurrent, 51N earth fault, 27 undervoltage, 59 overvoltage.</summary>
public enum GridProtectionFunction { InstantaneousOvercurrent50, Overcurrent51, EarthFault51N, Undervoltage27, Overvoltage59 }
