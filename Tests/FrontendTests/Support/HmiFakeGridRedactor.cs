using Domain.Grid.Snapshots;

namespace FrontendTests.Support;

/// <summary>Temporary test-only projection until the shared grid redactor is merged.</summary>
public static class HmiFakeGridRedactor
{
    public static GridSnapshot ForOperators(GridSnapshot snapshot)
    {
        double Reading(string id, GridMeasurementKind kind, double fallback) =>
            snapshot.Measurements.FirstOrDefault(m => m.ElementId == id && m.Kind == kind)?.ReportedValue ?? fallback;
        var buses = snapshot.Buses.Select(b => b with
        {
            VoltageKv = Reading(b.Id, GridMeasurementKind.Voltage, b.VoltageKv),
            VoltagePu = Reading(b.Id, GridMeasurementKind.Voltage, b.VoltageKv) / b.NominalVoltageKv
        }).ToArray();
        var branches = snapshot.Branches.Select(b => b with
        {
            Fault = GridEquipmentFault.None,
            CurrentA = Reading(b.Id, GridMeasurementKind.Current, b.CurrentA),
            ActivePowerFromMw = Reading(b.Id, GridMeasurementKind.ActivePower, b.ActivePowerFromMw),
            ReactivePowerFromMvar = Reading(b.Id, GridMeasurementKind.ReactivePower, b.ReactivePowerFromMvar),
            TapPosition = b.TapPosition is null ? null : (int)Reading(b.Id, GridMeasurementKind.TapPosition, b.TapPosition.Value)
        }).ToArray();
        return snapshot with
        {
            Buses = buses,
            Branches = branches,
            Switches = snapshot.Switches.Select(s => s with
            {
                Fault = GridEquipmentFault.None,
                Closed = Reading(s.Id, GridMeasurementKind.SwitchState, s.Closed ? 1 : 0) > 0.5
            }).ToArray(),
            Protections = snapshot.Protections.Select(p => p with { SettingsChanged = false }).ToArray(),
            Measurements = snapshot.Measurements.Select(m => m with
            {
                TrueValue = m.ReportedValue,
                IsSpoofed = false,
                SpoofedValue = null
            }).ToArray()
        };
    }
}
