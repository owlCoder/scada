using Domain.Process.Devices;

namespace Domain.Process.Configuration;

/// <summary>
/// Three-stage water treatment plant: raw water tank, treatment tank and clear-water reservoir,
/// connected by a duty/standby transfer pump pair and a treated-water pump.
/// </summary>
public static class DefaultPlantDefinition
{
    public static PlantDefinition Create() => new(
        "Water Treatment Plant",
        [
            new("T-101", "Raw Water Tank", 60, 55, new(8, 20, 85, 95)),
            new("T-102", "Treatment Tank", 40, 45, new(8, 20, 85, 95)),
            new("T-103", "Clear Water Reservoir", 80, 50, new(5, 15, 88, 96))
        ],
        [
            new("P-101", "Transfer Pump A (duty)", "T-101", "T-102", "V-102", 3.0, 6.0, 4),
            new("P-103", "Transfer Pump B (standby)", "T-101", "T-102", "V-103", 3.0, 6.0, 4),
            new("P-102", "Treated Water Pump", "T-102", "T-103", "V-201", 2.6, 5.5, 4)
        ],
        [
            new("V-101", "Raw Water Inlet Valve", ValveRole.Inlet, "T-101", 3.2, 20, 0, 100),
            new("V-102", "P-101 Discharge Valve", ValveRole.Discharge, null, 0, 25, 0, 100),
            new("V-103", "P-103 Discharge Valve", ValveRole.Discharge, null, 0, 25, 0, 100),
            new("V-201", "P-102 Discharge Valve", ValveRole.Discharge, null, 0, 25, 0, 100),
            new("V-301", "Distribution Outlet Valve", ValveRole.Outlet, "T-103", 3.0, 15, 0, 70),
            new("V-302", "Treatment Drain Valve", ValveRole.Drain, "T-102", 2.0, 20, 0, 0)
        ],
        [
            new("LT-101", "T-101 Level", SensorKind.Level, "T-101", "%", 0, 100),
            new("LT-102", "T-102 Level", SensorKind.Level, "T-102", "%", 0, 100),
            new("LT-103", "T-103 Level", SensorKind.Level, "T-103", "%", 0, 100),
            new("FT-101", "Raw Water Inflow", SensorKind.Flow, "V-101", "m3/min", 0, 5),
            new("FT-102", "P-101 Discharge Flow", SensorKind.Flow, "P-101", "m3/min", 0, 5),
            new("FT-104", "P-103 Discharge Flow", SensorKind.Flow, "P-103", "m3/min", 0, 5),
            new("FT-103", "P-102 Discharge Flow", SensorKind.Flow, "P-102", "m3/min", 0, 5),
            new("FT-301", "Distribution Outflow", SensorKind.Flow, "V-301", "m3/min", 0, 5),
            new("FT-302", "Treatment Drain Flow", SensorKind.Flow, "V-302", "m3/min", 0, 5),
            new("PT-101", "P-101 Discharge Pressure", SensorKind.Pressure, "P-101", "bar", 0, 10),
            new("PT-103", "P-103 Discharge Pressure", SensorKind.Pressure, "P-103", "bar", 0, 10),
            new("PT-102", "P-102 Discharge Pressure", SensorKind.Pressure, "P-102", "bar", 0, 10)
        ],
        new ProcessLimits(MaxDischargePressureBar: 5.0, DeadheadTripSeconds: 6, DeadheadValvePercent: 5));
}
