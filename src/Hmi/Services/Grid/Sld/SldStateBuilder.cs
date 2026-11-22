using System.Globalization;
using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using Domain.Process.Devices;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Resolves the dynamic per-element state from a (possibly operator-redacted) snapshot. Only reported values and
/// qualities are used — never true values or spoof indicators, so the result is safe to show to operators.
/// </summary>
public static class SldStateBuilder
{
    public static IReadOnlyDictionary<string, SldElementState> Build(
        GridSnapshot snapshot, GridDefinition definition, IReadOnlyList<GridTag> tags, string? selectedId)
    {
        var result = new Dictionary<string, SldElementState>();
        foreach (var bus in definition.Buses)
        {
            var snap = snapshot.Bus(bus.Id);
            var quality = Quality(snapshot, bus.Id, GridMeasurementKind.Voltage);
            var kV = snap is null ? null : Invariant($"{snap.VoltageKv:0.00} kV");
            result[bus.Id] = new SldElementState(bus.Id, snap?.Energized == true, null, quality == MeasurementQuality.Bad,
                Flag(quality), kV, null, false, false, false,
                Alarm(snapshot, definition, bus.Id), Unack(snapshot, definition, bus.Id), Tags(tags, bus.Id), bus.Id == selectedId,
                KeyText: kV);
        }
        foreach (var line in definition.Lines)
            result[line.Id] = Branch(snapshot, definition, tags, selectedId, line.Id, line.Name, line.FromBusId, line.ToBusId);
        foreach (var transformer in definition.Transformers)
            result[transformer.Id] = Branch(snapshot, definition, tags, selectedId, transformer.Id, transformer.Name,
                transformer.HvBusId, transformer.LvBusId);
        foreach (var source in definition.Sources)
        {
            var snap = snapshot.Bus(source.BusId);
            result[source.Id] = new SldElementState(source.Id, snap?.Energized == true, null, false, null, null, null,
                false, false, false, Alarm(snapshot, definition, source.Id), Unack(snapshot, definition, source.Id),
                Tags(tags, source.Id), source.Id == selectedId);
        }
        foreach (var load in definition.Loads)
        {
            var snap = snapshot.Load(load.Id);
            var quality = Quality(snapshot, load.Id, GridMeasurementKind.ActivePower);
            var power = snap?.ActivePowerMw ?? 0;
            result[load.Id] = new SldElementState(load.Id, snap?.Energized == true, null, quality == MeasurementQuality.Bad,
                Flag(quality), snap is null ? null : Invariant($"{power:0.00} MW"), null, snap?.Energized == true && Math.Abs(power) > 0.001,
                power > 0, false, Alarm(snapshot, definition, load.Id), Unack(snapshot, definition, load.Id),
                Tags(tags, load.Id), load.Id == selectedId);
        }
        foreach (var generator in definition.Generators)
        {
            var snap = snapshot.Generators.FirstOrDefault(g => g.Id == generator.Id);
            var quality = Quality(snapshot, generator.Id, GridMeasurementKind.ActivePower);
            var power = snap?.ActivePowerMw ?? 0;
            result[generator.Id] = new SldElementState(generator.Id, snap?.Connected == true, null, quality == MeasurementQuality.Bad,
                Flag(quality), snap is null ? null : Invariant($"{power:0.00} MW"), null, snap?.Connected == true && Math.Abs(power) > 0.001,
                power <= 0, false, Alarm(snapshot, definition, generator.Id), Unack(snapshot, definition, generator.Id),
                Tags(tags, generator.Id), generator.Id == selectedId);
        }
        foreach (var capacitor in definition.Capacitors)
        {
            var snap = snapshot.Capacitors.FirstOrDefault(c => c.Id == capacitor.Id);
            var energized = snapshot.Bus(capacitor.BusId)?.Energized == true;
            result[capacitor.Id] = new SldElementState(capacitor.Id, energized, null, false, null,
                snap is null ? null : Invariant($"{snap.ReactivePowerMvar:0.00} Mvar · {snap.StepsInService}/{snap.Steps}"), null,
                false, false, false, Alarm(snapshot, definition, capacitor.Id), Unack(snapshot, definition, capacitor.Id),
                Tags(tags, capacitor.Id), capacitor.Id == selectedId);
        }
        foreach (var switchDef in definition.Switches)
        {
            var snap = snapshot.Switch(switchDef.Id);
            var quality = Quality(snapshot, switchDef.Id, GridMeasurementKind.SwitchState);
            var closed = quality == MeasurementQuality.Bad ? null : snap?.Closed;
            var tripped = snapshot.Protections.Any(p => p.SwitchId == switchDef.Id && p.Tripped);
            var busEnergized = snapshot.Bus(switchDef.BusId)?.Energized == true;
            result[switchDef.Id] = new SldElementState(switchDef.Id, busEnergized && closed == true, closed,
                closed is null, Flag(quality), closed is null ? "?" : closed == true ? "closed" : "open", null,
                false, false, tripped, Alarm(snapshot, definition, switchDef.Id), Unack(snapshot, definition, switchDef.Id),
                Tags(tags, switchDef.Id), switchDef.Id == selectedId);
        }
        return result;
    }

    private static SldElementState Branch(GridSnapshot snapshot, GridDefinition definition, IReadOnlyList<GridTag> tags,
        string? selectedId, string id, string name, string fromBusId, string toBusId)
    {
        var branch = snapshot.Branch(id);
        var quality = Quality(snapshot, id, GridMeasurementKind.Current, GridMeasurementKind.ActivePower);
        var energized = branch?.Energized == true || (snapshot.Bus(fromBusId)?.Energized == true && snapshot.Bus(toBusId)?.Energized == true);
        var power = branch?.ActivePowerFromMw ?? 0;
        var key = branch is null ? null : Invariant($"{branch.CurrentA:0.0} A · {branch.LoadingPercent:0} %");
        // The tap position gets its own placed value box at high zoom (see SldElementG), not a suffix here.
        var value = key;
        return new SldElementState(id, energized, null, quality == MeasurementQuality.Bad, Flag(quality), value,
            branch?.LoadingPercent, energized && Math.Abs(power) > 0.001, power > 0, false,
            Alarm(snapshot, definition, id), Unack(snapshot, definition, id), Tags(tags, id), id == selectedId,
            KeyText: key);
    }

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);

    private static MeasurementQuality? Quality(GridSnapshot snapshot, string elementId, params GridMeasurementKind[] kinds) =>
        snapshot.Measurements.Where(m => m.ElementId == elementId && kinds.Contains(m.Kind))
            .Select(m => m.Quality)
            .OrderByDescending(q => q)
            .FirstOrDefault();

    private static string? Flag(MeasurementQuality? quality) => quality switch
    {
        MeasurementQuality.Bad => "B",
        MeasurementQuality.Uncertain => "U",
        _ => null
    };

    private static IReadOnlyList<GridTag> Tags(IReadOnlyList<GridTag> tags, string elementId) =>
        tags.Where(t => t.ElementId == elementId).ToArray();

    private static AlarmSeverity? Alarm(GridSnapshot snapshot, GridDefinition definition, string elementId)
    {
        var ids = new HashSet<string> { elementId };
        foreach (var protection in definition.Protections.Where(p => p.SwitchId == elementId))
            ids.Add(protection.Id);
        return snapshot.Alarms.Where(a => a.Active && ids.Contains(a.SourceId))
            .Select(a => (AlarmSeverity?)a.Severity)
            .Max();
    }

    private static bool Unack(GridSnapshot snapshot, GridDefinition definition, string elementId)
    {
        var ids = new HashSet<string> { elementId };
        foreach (var protection in definition.Protections.Where(p => p.SwitchId == elementId))
            ids.Add(protection.Id);
        return snapshot.Alarms.Any(a => a.Active && !a.Acknowledged && ids.Contains(a.SourceId));
    }
}
