namespace Domain.Grid.Definition;

/// <summary>A compact Central-European 110/20/0.4 kV training distribution network.</summary>
public static class DefaultGridDefinition
{
    public static GridDefinition Create()
    {
        var buses = new List<GridBusDefinition>
        {
            new("HV", "110 kV infeed", 110, GridBusKind.Busbar, new(1, 6)),
            new("MV", "20 kV busbar", 20, GridBusKind.Busbar, new(7, 6))
        };
        var lines = new List<GridLineDefinition>();
        var transformers = new List<GridTransformerDefinition>
        {
            new("TR1", "110/20 kV TR1", "HV", "MV", 31.5, 110, 20, 12, 145, 22,
                true, -10, 10, 0, 2, 1.5, 20.5, 1.5, 30, "ONAN 31.5 MVA", new(4, 6))
        };
        var switches = new List<GridSwitchDefinition>
        {
            new("Q0", "TR1 LV incomer", GridSwitchKind.Breaker, "MV", "TR1", true, 25, new(6, 6)),
            new("DS0", "TR1 HV disconnector", GridSwitchKind.Disconnector, "HV", "TR1", true, null, new(2, 6))
        };
        var loads = new List<GridLoadDefinition>();
        var protections = new List<GridProtectionDefinition>
        {
            new("Q0-51", "Incomer backup 51", "Q0", GridProtectionFunction.Overcurrent51,
                GridCurveType.DefiniteTime, 1200, 1.2, true),
            new("MV-27", "Busbar undervoltage 27", "Q0", GridProtectionFunction.Undervoltage27,
                GridCurveType.DefiniteTime, 0.8, 5, true)
        };
        var rtus = new List<GridRtuDefinition>();
        for (var f = 1; f <= 3; f++)
        {
            var ids = new List<string>();
            var previous = "MV";
            var count = f == 3 ? 3 : 4;
            for (var n = 1; n <= count; n++)
            {
                var id = $"F{f}-{n}";
                var mv = $"{id}-MV";
                var lv = $"{id}-LV";
                var y = 2 + (f - 1) * 5;
                var x = 11 + (n - 1) * 6;
                buses.Add(new(mv, $"TS {f}{n} 20 kV", 20, GridBusKind.Junction, new(x, y)));
                buses.Add(new(lv, $"TS {f}{n} 0.4 kV", 0.4, GridBusKind.ConsumerPoint, new(x, y + 2)));
                var cable = f == 1 || (f == 2 && n <= 2);
                lines.Add(new(id, $"Feeder {f} section {n}", previous, mv,
                    cable ? GridLineKind.Cable : GridLineKind.Overhead, cable ? 0.65 : 1.2,
                    cable ? 0.206 : 0.306, cable ? 0.122 : 0.365, cable ? 90 : 3,
                    cable ? 300 : 330, cable ? "NA2XS(F)2Y 3x1x150" : "Al/Fe 95/15", []));
                var large = n == 2;
                var tr = $"TS{f}{n}";
                transformers.Add(new(tr, $"TS {f}{n} 20/0.4 kV", mv, lv, large ? 1 : 0.63,
                    20, 0.4, 6, large ? 10.5 : 6.5, large ? 1.5 : 1,
                    false, 0, 0, 0, 0, 0, null, 0, 0, "Dyn11", new(x, y + 1)));
                var load = $"L{f}{n}";
                loads.Add(new(load, $"TS {f}{n} consumers", lv, large ? 0.68 : 0.38,
                    0.96, large ? GridLoadProfile.Commercial : GridLoadProfile.Residential,
                    large ? 85 : 190, f == 1 && n == 2, new(x + 1, y + 2)));
                switches.Add(new($"DS{f}{n}", $"TS {f}{n} isolator", GridSwitchKind.Disconnector,
                    mv, tr, true, null, new(x - 1, y + 1)));
                ids.AddRange([id, mv, lv, tr, load, $"DS{f}{n}"]);
                previous = mv;
            }
            var breaker = $"QF{f}";
            switches.Add(new(breaker, $"F{f} breaker", GridSwitchKind.Breaker, "MV", $"F{f}-1", true, 25, new(9, 2 + (f - 1) * 5)));
            ids.Add(breaker);
            foreach (var (suffix, function, pickup, delay, curve) in new[]
            {
                ("51", GridProtectionFunction.Overcurrent51, 220d, 0.15, GridCurveType.IecStandardInverse),
                ("50", GridProtectionFunction.InstantaneousOvercurrent50, 1500d, 0.08, GridCurveType.DefiniteTime),
                ("51N", GridProtectionFunction.EarthFault51N, 50d, 0.2, GridCurveType.DefiniteTime)
            })
            {
                var relay = $"{breaker}-{suffix}";
                protections.Add(new(relay, $"F{f} {suffix}", breaker, function, curve, pickup, delay, true));
                ids.Add(relay);
            }
            rtus.Add(new($"RTU{f + 1}", $"Feeder {f} RTU", f + 1, null, ids));
        }
        // A short physical link with one normally-open load-break switch restores either feeder end.
        lines.Add(new("TIE-LINE", "F2/F3 tie cable", "F2-4-MV", "F3-3-MV", GridLineKind.Cable,
            0.3, 0.206, 0.122, 90, 300, "NA2XS(F)2Y 3x1x150", []));
        switches.Add(new("TIE", "F2/F3 normally-open tie", GridSwitchKind.LoadBreakSwitch,
            "F2-4-MV", "TIE-LINE", false, null, new(31, 10)));
        // Industrial aggregation replaces one residential group.
        loads[^1] = loads[^1] with { PeakActivePowerMw = 0.42, Profile = GridLoadProfile.Industrial, Customers = 12 };
        var generators = new GridGeneratorDefinition[]
        {
            new("PV1", "Urban rooftop PV", "F1-2-LV", GridGeneratorKind.Photovoltaic, 0.8, new(18, 4)),
            new("PV2", "Rural solar park", "F3-2-MV", GridGeneratorKind.Photovoltaic, 1.5, new(18, 12))
        };
        var capacitors = new GridCapacitorDefinition[] { new("CAP1", "20 kV compensation", "MV", 2, 4, 0, new(7, 15)) };
        rtus[0] = rtus[0] with { ElementIds = rtus[0].ElementIds.Concat(["PV1"]).ToArray() };
        rtus[1] = rtus[1] with { ElementIds = rtus[1].ElementIds.Concat(["TIE", "TIE-LINE"]).ToArray() };
        rtus[2] = rtus[2] with { ElementIds = rtus[2].ElementIds.Concat(["PV2"]).ToArray() };
        rtus.Insert(0, new("RTU1", "Primary substation RTU", 1, null, ["HV", "MV", "GRID", "TR1", "Q0", "DS0", "CAP1", "Q0-51", "MV-27"]));
        return GridProtectionDefaults.Add(new("Central European distribution grid", 50, buses,
            [new("GRID", "110 kV grid", "HV", 1, 2500, new(0, 6))], lines, transformers,
            switches, loads, generators, capacitors, protections, rtus));
    }
}
