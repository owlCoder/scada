namespace Domain.Grid.Definition;

public static class GridDefinitionValidator
{
    public static IReadOnlyList<string> Validate(GridDefinition definition)
    {
        if (definition is null) return ["Definition is required."];
        if (definition.Buses is null || definition.Sources is null || definition.Lines is null || definition.Transformers is null
            || definition.Switches is null || definition.Loads is null || definition.Generators is null || definition.Capacitors is null
            || definition.Protections is null || definition.Rtus is null) return ["All definition collections are required."];
        if (definition.Buses.Any(x => x is null) || definition.Sources.Any(x => x is null) || definition.Lines.Any(x => x is null)
            || definition.Transformers.Any(x => x is null) || definition.Switches.Any(x => x is null) || definition.Loads.Any(x => x is null)
            || definition.Generators.Any(x => x is null) || definition.Capacitors.Any(x => x is null) || definition.Protections.Any(x => x is null)
            || definition.Rtus.Any(x => x is null)) return ["Definition elements must not be null."];
        var errors = new List<string>();
        var d = definition;
        var allIds = d.Buses.Select(x => x.Id).Concat(d.Sources.Select(x => x.Id)).Concat(d.Lines.Select(x => x.Id))
            .Concat(d.Transformers.Select(x => x.Id)).Concat(d.Switches.Select(x => x.Id)).Concat(d.Loads.Select(x => x.Id))
            .Concat(d.Generators.Select(x => x.Id)).Concat(d.Capacitors.Select(x => x.Id)).Concat(d.Protections.Select(x => x.Id))
            .Concat(d.Rtus.Select(x => x.Id)).ToArray();
        foreach (var group in allIds.GroupBy(x => x))
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
                errors.Add($"Invalid or duplicate id: {group.Key}.");
        if (errors.Count > 0) return errors;
        if (d.Lines.Any(x => string.IsNullOrWhiteSpace(x.FromBusId) || string.IsNullOrWhiteSpace(x.ToBusId))
            || d.Transformers.Any(x => string.IsNullOrWhiteSpace(x.HvBusId) || string.IsNullOrWhiteSpace(x.LvBusId))
            || d.Sources.Any(x => string.IsNullOrWhiteSpace(x.BusId)) || d.Switches.Any(x => string.IsNullOrWhiteSpace(x.BusId) || string.IsNullOrWhiteSpace(x.ElementId))
            || d.Loads.Any(x => string.IsNullOrWhiteSpace(x.BusId)) || d.Generators.Any(x => string.IsNullOrWhiteSpace(x.BusId))
            || d.Capacitors.Any(x => string.IsNullOrWhiteSpace(x.BusId)) || d.Protections.Any(x => string.IsNullOrWhiteSpace(x.SwitchId)))
            return ["Element references must not be empty."];
        if (d.Rtus.Any(r => r.ElementIds is null) || d.Buses.Any(b => b.Position is null)) return ["RTU element lists and diagram positions are required."];
        if (d.Buses.Any(x => !Enum.IsDefined(x.Kind)) || d.Lines.Any(x => !Enum.IsDefined(x.Kind))
            || d.Switches.Any(x => !Enum.IsDefined(x.Kind)) || d.Generators.Any(x => !Enum.IsDefined(x.Kind))
            || d.Loads.Any(x => !Enum.IsDefined(x.Profile)) || d.Protections.Any(x => !Enum.IsDefined(x.Function) || !Enum.IsDefined(x.Curve)))
            errors.Add("Unknown equipment kind, load profile or protection function/curve.");
        var buses = d.Buses.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        var terminals = new Dictionary<string, string[]>();
        foreach (var line in d.Lines)
        {
            terminals.TryAdd(line.Id, [line.FromBusId, line.ToBusId]);
            CheckBus(line.FromBusId, line.Id); CheckBus(line.ToBusId, line.Id);
            if (buses.TryGetValue(line.FromBusId, out var a) && buses.TryGetValue(line.ToBusId, out var b)
                && Math.Abs(a.NominalVoltageKv - b.NominalVoltageKv) > 0.001)
                errors.Add($"{line.Id}: line ends must have the same nominal voltage.");
            if (line.FromBusId == line.ToBusId || !Positive(line.LengthKm, line.ReactanceOhmPerKm, line.RatedCurrentA)
                || !Nonnegative(line.ResistanceOhmPerKm, line.SusceptanceMicroSiemensPerKm))
                errors.Add($"{line.Id}: invalid line parameters.");
            if (line.LengthKm > 100 || line.RatedCurrentA > 5000) errors.Add($"Warning: {line.Id}: unusual line rating or length.");
        }
        foreach (var t in d.Transformers)
        {
            terminals.TryAdd(t.Id, [t.HvBusId, t.LvBusId]);
            CheckBus(t.HvBusId, t.Id); CheckBus(t.LvBusId, t.Id);
            if (!Positive(t.RatedPowerMva, t.HvRatedKv, t.LvRatedKv, t.ShortCircuitVoltagePercent)
                || t.ShortCircuitVoltagePercent >= 100 || !Nonnegative(t.LoadLossKw, t.NoLoadLossKw)
                || t.LoadLossKw / (1000 * t.RatedPowerMva) >= t.ShortCircuitVoltagePercent / 100
                || t.HvRatedKv <= t.LvRatedKv || t.HvBusId == t.LvBusId)
                errors.Add($"{t.Id}: invalid transformer impedance or rating.");
            foreach (var (id, kv) in new[] { (t.HvBusId, t.HvRatedKv), (t.LvBusId, t.LvRatedKv) })
                if (buses.TryGetValue(id, out var bus) && Math.Abs(kv / bus.NominalVoltageKv - 1) > 0.1)
                    errors.Add($"{t.Id}: winding voltage does not match bus {id} within 10%.");
            if (t.TapMin > t.TapMax || t.InitialTap < t.TapMin || t.InitialTap > t.TapMax
                || t.TapNeutral < t.TapMin || t.TapNeutral > t.TapMax || !Nonnegative(t.TapStepPercent)
                || 1 + (t.TapMin - t.TapNeutral) * t.TapStepPercent / 100 <= 0
                || (t.HasOnLoadTapChanger && (!Positive(t.TapStepPercent, t.AvrDeadbandPercent, t.AvrDelaySeconds)))
                || (t.AvrTargetLvKv is { } target && !Positive(target)))
                errors.Add($"{t.Id}: invalid tap/AVR settings.");
        }
        foreach (var bus in d.Buses)
            if (!Positive(bus.NominalVoltageKv, bus.MinVoltagePu, bus.MaxVoltagePu) || bus.MinVoltagePu >= bus.MaxVoltagePu
                || !double.IsFinite(bus.Position.X) || !double.IsFinite(bus.Position.Y))
                errors.Add($"{bus.Id}: invalid bus voltage, limits or position.");
        foreach (var s in d.Sources)
        {
            CheckBus(s.BusId, s.Id);
            if (!Positive(s.ShortCircuitPowerMva, s.VoltageSetpointPu) || s.VoltageSetpointPu is < 0.8 or > 1.2)
                errors.Add($"{s.Id}: invalid source parameters.");
        }
        foreach (var l in d.Loads)
        {
            terminals.TryAdd(l.Id, [l.BusId]); CheckBus(l.BusId, l.Id);
            if (!Nonnegative(l.PeakActivePowerMw) || !Positive(l.PowerFactor) || l.PowerFactor > 1 || l.Customers < 0)
                errors.Add($"{l.Id}: invalid load parameters.");
        }
        foreach (var g in d.Generators)
        {
            terminals.TryAdd(g.Id, [g.BusId]); CheckBus(g.BusId, g.Id);
            if (!Nonnegative(g.RatedPowerMw)) errors.Add($"{g.Id}: invalid generation rating.");
        }
        foreach (var c in d.Capacitors)
        {
            terminals.TryAdd(c.Id, [c.BusId]); CheckBus(c.BusId, c.Id);
            if (!Positive(c.RatedPowerMvar) || c.Steps < 1 || c.InitialSteps < 0 || c.InitialSteps > c.Steps)
                errors.Add($"{c.Id}: invalid capacitor steps or rating.");
        }
        foreach (var s in d.Switches)
        {
            CheckBus(s.BusId, s.Id);
            if (!terminals.TryGetValue(s.ElementId, out var ends) || !ends.Contains(s.BusId))
                errors.Add($"{s.Id}: switch must connect to a terminal of {s.ElementId}.");
            if (s.BreakingCapacityKa is { } capacity && !Positive(capacity)) errors.Add($"{s.Id}: invalid breaking capacity.");
        }
        foreach (var p in d.Protections)
        {
            if (!d.Switches.Any(s => s.Id == p.SwitchId && s.Kind == GridSwitchKind.Breaker))
                errors.Add($"{p.Id}: protection must trip a breaker.");
            if (!Positive(p.PickupValue) || !Nonnegative(p.TimeSetting)) errors.Add($"{p.Id}: invalid relay settings.");
        }
        foreach (var rtu in d.Rtus)
        {
            if (rtu.CommonAddress is < 1 or > 65534) errors.Add($"{rtu.Id}: invalid RTU common address.");
            foreach (var id in rtu.ElementIds)
                if (string.IsNullOrWhiteSpace(id) || !allIds.Contains(id)) errors.Add($"{rtu.Id}: unknown element {id}.");
        }
        if (d.Rtus.Select(x => x.CommonAddress).Distinct().Count() != d.Rtus.Count) errors.Add("RTU common addresses must be unique.");
        if (d.Sources.Count == 0) errors.Add("At least one source is required.");
        if (string.IsNullOrWhiteSpace(d.Name) || !Positive(d.NominalFrequencyHz)) errors.Add("Invalid grid name or frequency.");
        foreach (var id in allIds.Where(id => !d.Rtus.Any(r => r.ElementIds.Contains(id)) && !d.Rtus.Any(r => r.Id == id)))
            errors.Add($"Warning: {id}: no RTU assignment.");
        return errors;

        bool CheckBus(string id, string owner)
        {
            if (string.IsNullOrWhiteSpace(id) || !buses.ContainsKey(id)) errors.Add($"{owner}: unknown bus {id}.");
            return !string.IsNullOrWhiteSpace(id) && buses.ContainsKey(id);
        }
    }

    public static bool HasErrors(IEnumerable<string> issues) => issues.Any(x => !x.StartsWith("Warning:", StringComparison.Ordinal));
    private static bool Positive(params double[] values) => values.All(x => double.IsFinite(x) && x > 0);
    private static bool Nonnegative(params double[] values) => values.All(x => double.IsFinite(x) && x >= 0);
}
