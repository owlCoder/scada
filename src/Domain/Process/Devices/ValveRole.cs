namespace Domain.Process.Devices;

/// <summary>
/// Inlet valves feed a tank from the external raw-water source, discharge valves sit on a pump
/// discharge line, outlet and drain valves release water from a tank by gravity.
/// </summary>
public enum ValveRole { Inlet, Discharge, Outlet, Drain }
