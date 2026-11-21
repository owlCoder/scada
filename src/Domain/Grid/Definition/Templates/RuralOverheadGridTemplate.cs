namespace Domain.Grid.Definition.Templates;

/// <summary>
/// A rural 20 kV overhead network fed from one 110/20 kV 20 MVA transformer with an OLTC/AVR (the long overhead
/// feeders need the regulator for voltage drop). Three long Al/Fe 95/15 feeders carry ten pole-mounted 20/0.4 kV
/// transformers (100/250/400 kVA) with residential load, and two large PV parks at the feeder ends cause reverse
/// power flow at midday (a deliberate, realistic exception: the 08:00/evening-peak operating points stay inside
/// limits). Feeder breakers carry a reclosing-style protection scheme (50/51/51N only — the engine has no automatic
/// reclose logic). A 20 kV capacitor bank at the primary substation covers the <c>AllCapacitors</c> scenario target.
/// Left-to-right single-line diagram.
/// </summary>
public static class RuralOverheadGridTemplate
{
    public const string TemplateId = "rural-overhead-20kv";

    public static GridDefinition Create()
    {
        var buses = new List<GridBusDefinition>
        {
            new("HV", "110 kV infeed", 110, GridBusKind.Busbar, new(2, 8)),
            new("MV", "20 kV busbar", 20, GridBusKind.Busbar, new(7, 8))
        };
        var lines = new List<GridLineDefinition>();
        var transformers = new List<GridTransformerDefinition>
        {
            new("TR1", "110/20 kV 20 MVA with OLTC", "HV", "MV", 20, 110, 20, 11, 120, 15,
                true, -8, 8, 0, 0, 1.25, 20.4, 1.2, 30, "ONAN 20 MVA", new(5, 8))
        };
        var switches = new List<GridSwitchDefinition>
        {
            new("DS0", "TR1 HV disconnector", GridSwitchKind.Disconnector, "HV", "TR1", true, null, new(3, 8)),
            new("Q0", "TR1 20 kV incomer", GridSwitchKind.Breaker, "MV", "TR1", true, 25, new(6, 8))
        };
        var loads = new List<GridLoadDefinition>();
        var protections = new List<GridProtectionDefinition>
        {
            new("Q0-51", "Incomer backup 51", "Q0", GridProtectionFunction.Overcurrent51, GridCurveType.DefiniteTime, 700, 1.2, true),
            new("Q0-50", "Incomer 50", "Q0", GridProtectionFunction.InstantaneousOvercurrent50, GridCurveType.DefiniteTime, 6000, 0.05, true),
            new("MV-27", "Busbar undervoltage 27", "Q0", GridProtectionFunction.Undervoltage27, GridCurveType.DefiniteTime, 0.85, 4, true)
        };
        var rtus = new List<GridRtuDefinition>();
        var feederRtuIds = new List<string>[3];
        var generators = new List<GridGeneratorDefinition>();

        // One long overhead feeder chain with pole-mounted substations at every junction node.
        List<string> AddFeeder(int index, string feeder, int y, double[] sectionKm,
            (string Node, double Kva, double PeakMw, int Customers)[] stations, double? pvMw, string pvId)
        {
            var ids = new List<string> { $"{feeder}-QF" };
            var previous = "MV";
            for (var n = 1; n <= sectionKm.Length; n++)
            {
                var node = stations[n - 1].Node;
                var x = 12 + (n - 1) * 5;
                var mv = $"{node}-MV";
                var lv = $"{node}-LV";
                buses.Add(new(mv, $"{node} 20 kV pole station", 20, GridBusKind.Junction, new(x, y)));
                buses.Add(new(lv, $"{node} 0.4 kV", 0.4, GridBusKind.ConsumerPoint, new(x, y + 2)));
                var lineId = $"{feeder}-{n}";
                var spur = n == sectionKm.Length && feeder == "FC";
                lines.Add(new(lineId, $"{feeder} overhead section {n}", previous, mv, GridLineKind.Overhead,
                    sectionKm[n - 1], spur ? 0.59 : 0.306, spur ? 0.4 : 0.365, spur ? 2.8 : 3, spur ? 210 : 330,
                    spur ? "Al/Fe 50/8" : "Al/Fe 95/15", []));
                var station = stations[n - 1];
                var tr = $"{node}-TR";
                var kva = station.Kva;
                transformers.Add(new(tr, $"{node} pole-mounted 20/0.4 kV {kva:0} kVA", mv, lv, kva / 1000, 20, 0.4,
                    kva >= 400 ? 5 : kva >= 250 ? 4.5 : 4, kva >= 400 ? 4.6 : kva >= 250 ? 3.3 : 1.8,
                    kva >= 400 ? 0.5 : kva >= 250 ? 0.35 : 0.25, false, 0, 0, 0, 0, 0, null, 0, 0,
                    kva >= 400 ? "Dyn5 400 kVA" : kva >= 250 ? "Dyn5 250 kVA" : "Dyn5 100 kVA", new(x, y + 1)));
                var load = $"{node}-L";
                loads.Add(new(load, $"{node} village", lv, station.PeakMw, 0.95, GridLoadProfile.Residential, station.Customers, false, new(x + 1, y + 2)));
                var sw = $"{node}-SW";
                switches.Add(new(sw, $"{node} pole switch", GridSwitchKind.LoadBreakSwitch, mv, tr, true, null, new(x - 1, y + 1)));
                ids.AddRange([lineId, mv, lv, tr, load, sw]);
                previous = mv;
            }
            var qf = $"{feeder}-QF";
            switches.Add(new(qf, $"{feeder} feeder breaker (reclosing scheme)", GridSwitchKind.Breaker, "MV", $"{feeder}-1", true, 25, new(9, y)));
            foreach (var (suffix, function, pickup, time, curve) in new (string, GridProtectionFunction, double, double, GridCurveType)[]
            {
                ("51", GridProtectionFunction.Overcurrent51, 150, 0.25, GridCurveType.IecStandardInverse),
                ("50", GridProtectionFunction.InstantaneousOvercurrent50, 1800, 0.05, GridCurveType.DefiniteTime),
                ("51N", GridProtectionFunction.EarthFault51N, 25, 0.3, GridCurveType.DefiniteTime)
            })
            {
                var relay = $"{qf}-{suffix}";
                protections.Add(new(relay, $"{feeder} {suffix}", qf, function, curve, pickup, time, true));
                ids.Add(relay);
            }
            if (pvMw is { } pv)
            {
                var lastMv = $"{stations[^1].Node}-MV";
                generators.Add(new(pvId, $"{pvId} PV park", lastMv, GridGeneratorKind.Photovoltaic, pv,
                    new(12 + (stations.Length - 1) * 5, y - 2)));
                ids.Add(pvId);
            }
            feederRtuIds[index] = ids;
            return ids;
        }

        AddFeeder(0, "FA", 4, [6, 5, 4, 5],
        [
            ("N1", 400, 0.3, 55), ("N2", 250, 0.18, 35), ("N3", 250, 0.19, 38), ("N4", 100, 0.08, 18)
        ], 3.0, "PV1");
        AddFeeder(1, "FB", 8, [8, 6, 7],
        [
            ("N5", 250, 0.17, 30), ("N6", 400, 0.29, 52), ("N7", 100, 0.07, 15)
        ], 2.5, "PV2");
        AddFeeder(2, "FC", 12, [5, 4, 3],
        [
            ("N8", 100, 0.06, 12), ("N9", 250, 0.18, 33), ("N10", 400, 0.27, 48)
        ], null, "");

        var capacitors = new List<GridCapacitorDefinition>
        {
            new("CAP1", "20 kV capacitor bank", "MV", 1.8, 3, 0, new(7, 10))
        };

        rtus.Add(new("RTU1", "Primary substation RTU", 1, null,
            ["HV", "MV", "GRID", "TR1", "DS0", "Q0", "CAP1", "Q0-51", "Q0-50", "MV-27"]));
        rtus.Add(new("RTU2", "Feeder A RTU", 2, null, feederRtuIds[0]));
        rtus.Add(new("RTU3", "Feeder B RTU", 3, null, feederRtuIds[1]));
        rtus.Add(new("RTU4", "Feeder C RTU", 4, null, feederRtuIds[2]));

        return new("Rural 20 kV overhead grid (20 MVA, PV parks)", 50, buses,
            [new("GRID", "110 kV upstream grid", "HV", 1, 1500, new(0, 8))], lines, transformers,
            switches, loads, generators, capacitors, protections, rtus);
    }
}
