using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Application.Grid;

/// <summary>Projects only reported telemetry and observable switch/relay behaviour to the operator.</summary>
public static class GridViewRedactor
{
    public static GridSnapshot ForOperators(GridSnapshot snapshot, GridDefinition? definition = null)
    {
        double Reported(string id, GridMeasurementKind kind, double fallback = 0) =>
            snapshot.Measurements.FirstOrDefault(m => m.ElementId == id && m.Kind == kind)?.ReportedValue ?? fallback;
        var buses = snapshot.Buses.Select(b =>
        {
            var voltage = Reported(b.Id, GridMeasurementKind.Voltage);
            return b with { VoltageKv = voltage, VoltagePu = voltage / b.NominalVoltageKv, Energized = voltage > b.NominalVoltageKv * 0.01, AngleDegrees = 0 };
        }).ToArray();
        var loads = snapshot.Loads.Select(l =>
        {
            var power = Reported(l.Id, GridMeasurementKind.ActivePower);
            var on = buses.First(b => b.Id == l.BusId).Energized && Math.Abs(power) > 1e-9;
            return l with { DemandMw = power, ActivePowerMw = power, ReactivePowerMvar = Reported(l.Id, GridMeasurementKind.ReactivePower),
                Energized = on, CustomersSupplied = on ? l.Customers : 0 };
        }).ToArray();
        return snapshot with
        {
            Buses = buses,
            Converged = true, SolverIterations = 0,
            Branches = snapshot.Branches.Select(b => b with
            {
                Fault = GridEquipmentFault.None, CurrentA = Reported(b.Id, GridMeasurementKind.Current),
                ActivePowerFromMw = Reported(b.Id, GridMeasurementKind.ActivePower), ReactivePowerFromMvar = Reported(b.Id, GridMeasurementKind.ReactivePower),
                ActivePowerToMw = -Reported(b.Id, GridMeasurementKind.ActivePower), ReactivePowerToMvar = -Reported(b.Id, GridMeasurementKind.ReactivePower),
                LossesMw = 0, Energized = buses.First(x => x.Id == b.FromBusId).Energized && buses.First(x => x.Id == b.ToBusId).Energized,
                LoadingPercent = Loading(b), TapPosition = b.TapPosition.HasValue ? (int)Reported(b.Id, GridMeasurementKind.TapPosition) : null
            }).ToArray(),
            Switches = snapshot.Switches.Select(s => s with { Fault = GridEquipmentFault.None,
                Closed = Reported(s.Id, GridMeasurementKind.SwitchState, s.Closed ? 1 : 0) >= 0.5,
                LastOperatedBy = s.LastCause == GridChangeCause.Unattributed ? null : s.LastOperatedBy }).ToArray(),
            Loads = loads,
            Generators = snapshot.Generators.Select(g => g with { ActivePowerMw = Reported(g.Id, GridMeasurementKind.ActivePower),
                Connected = buses.First(b => b.Id == g.BusId).Energized }).ToArray(),
            Protections = snapshot.Protections.Select(p =>
            {
                var original = definition?.Protections.FirstOrDefault(x => x.Id == p.Id);
                return p with { SettingsChanged = false, PickupValue = original?.PickupValue ?? 0, TimeSetting = original?.TimeSetting ?? 0,
                    Enabled = original?.Enabled ?? true };
            }).ToArray(),
            Measurements = snapshot.Measurements.Select(m => m with { TrueValue = m.ReportedValue, IsSpoofed = false, SpoofedValue = null }).ToArray(),
            Totals = snapshot.Totals with { LoadMw = loads.Sum(l => l.ActivePowerMw),
                GenerationMw = snapshot.Generators.Sum(g => Reported(g.Id, GridMeasurementKind.ActivePower)),
                SourceImportMw = 0, LossesMw = 0, CustomersSupplied = loads.Sum(l => l.CustomersSupplied) }
        };

        double Loading(BranchSnapshot branch)
        {
            var line = definition?.Lines.FirstOrDefault(l => l.Id == branch.Id);
            if (line is not null) return Reported(branch.Id, GridMeasurementKind.Current) / line.RatedCurrentA * 100;
            var transformer = definition?.Transformers.FirstOrDefault(t => t.Id == branch.Id);
            if (transformer is not null) return Math.Sqrt(Math.Pow(Reported(branch.Id, GridMeasurementKind.ActivePower), 2)
                + Math.Pow(Reported(branch.Id, GridMeasurementKind.ReactivePower), 2)) / transformer.RatedPowerMva * 100;
            return 0;
        }
    }
}
