namespace Domain.Grid.Definition.Templates;

/// <summary>
/// A 20 kV urban cable network fed from one 110/20 kV primary substation with two 40 MVA transformers on separate
/// busbar sections joined by a bus coupler. Four cable feeders form two open rings (each ring is two feeders tied at
/// their far ends through a normally-open ring point). Fourteen compact 20/0.4 kV substations (1000/630 kVA) carry a
/// commercial-heavy load profile; a 20 kV capacitor bank compensates the reactive power. Realistic catalog cable
/// NA2XS(F)2Y 3x1x150 / 3x1x240, left-to-right single-line diagram.
/// </summary>
public static class UrbanCableGridTemplate
{
    public const string TemplateId = "urban-cable-20kv";

    public static GridDefinition Create()
    {
        var buses = new List<GridBusDefinition>
        {
            new("HV", "110 kV busbar", 110, GridBusKind.Busbar, new(2, 8)),
            new("MV-A", "20 kV busbar section A", 20, GridBusKind.Busbar, new(7, 4)),
            new("MV-B", "20 kV busbar section B", 20, GridBusKind.Busbar, new(7, 12))
        };
        var lines = new List<GridLineDefinition>
        {
            new("COUPLER", "Bus coupler cable", "MV-A", "MV-B", GridLineKind.Cable, 0.05, 0.125, 0.11, 110, 630,
                "NA2XS(F)2Y 3x1x240", [new(8, 8)])
        };
        var transformers = new List<GridTransformerDefinition>
        {
            // Initial taps are the AVR's settled positions at 08:00, so the simulation does not start with a burst of
            // corrective tap changes (which the tap-manipulation rule rightly treats as rapid operation).
            new("TR1", "110/20 kV TR1 40 MVA", "HV", "MV-A", 40, 110, 20, 12.5, 210, 28,
                true, -10, 10, 0, 4, 0.9, 20.6, 1.5, 30, "ONAN 40 MVA", new(4, 6)),
            new("TR2", "110/20 kV TR2 40 MVA", "HV", "MV-B", 40, 110, 20, 12.5, 210, 28,
                true, -10, 10, 0, 3, 0.9, 20.6, 1.5, 30, "ONAN 40 MVA", new(4, 10))
        };
        var switches = new List<GridSwitchDefinition>
        {
            new("DS1", "TR1 HV disconnector", GridSwitchKind.Disconnector, "HV", "TR1", true, null, new(3, 7)),
            new("DS2", "TR2 HV disconnector", GridSwitchKind.Disconnector, "HV", "TR2", true, null, new(3, 9)),
            new("Q0A", "TR1 20 kV incomer", GridSwitchKind.Breaker, "MV-A", "TR1", true, 25, new(6, 5)),
            new("Q0B", "TR2 20 kV incomer", GridSwitchKind.Breaker, "MV-B", "TR2", true, 25, new(6, 11)),
            new("BC", "Bus coupler breaker", GridSwitchKind.Breaker, "MV-A", "COUPLER", true, 31.5, new(8, 8))
        };
        var loads = new List<GridLoadDefinition>();
        var protections = new List<GridProtectionDefinition>
        {
            new("Q0A-51", "TR1 incomer backup 51", "Q0A", GridProtectionFunction.Overcurrent51, GridCurveType.DefiniteTime, 500, 1, true),
            new("Q0A-50", "TR1 incomer 50", "Q0A", GridProtectionFunction.InstantaneousOvercurrent50, GridCurveType.DefiniteTime, 4000, 0.05, true),
            new("MV-A-27", "Busbar section A undervoltage 27", "Q0A", GridProtectionFunction.Undervoltage27, GridCurveType.DefiniteTime, 0.8, 5, true),
            new("Q0B-51", "TR2 incomer backup 51", "Q0B", GridProtectionFunction.Overcurrent51, GridCurveType.DefiniteTime, 500, 1, true),
            new("Q0B-50", "TR2 incomer 50", "Q0B", GridProtectionFunction.InstantaneousOvercurrent50, GridCurveType.DefiniteTime, 4000, 0.05, true),
            new("MV-B-27", "Busbar section B undervoltage 27", "Q0B", GridProtectionFunction.Undervoltage27, GridCurveType.DefiniteTime, 0.8, 5, true),
            new("BC-51", "Coupler backup 51", "BC", GridProtectionFunction.Overcurrent51, GridCurveType.DefiniteTime, 400, 0.8, true)
        };
        var rtus = new List<GridRtuDefinition>();
        var feederRtuIds = new List<string>[4];

        // Two feeders per ring, tied at their far ends by a normally-open ring point (open-ring operation).
        (List<string> Ids, string LastMv) AddFeeder(int index, string feeder, string bus, int y,
            (string Id, double Mva, double PeakMw, GridLoadProfile Profile, int Customers, bool Critical)[] stations)
        {
            var ids = new List<string>();
            var previous = bus;
            for (var n = 1; n <= stations.Length; n++)
            {
                var station = stations[n - 1];
                var x = 12 + (n - 1) * 5;
                var mv = $"{station.Id}-MV";
                var lv = $"{station.Id}-LV";
                buses.Add(new(mv, $"{station.Id} 20 kV ring node", 20, GridBusKind.Junction, new(x, y)));
                buses.Add(new(lv, $"{station.Id} 0.4 kV", 0.4, GridBusKind.ConsumerPoint, new(x, y + 2)));
                var lineId = $"{feeder}-C{n}";
                lines.Add(new(lineId, $"{feeder} cable section {n}", previous, mv, GridLineKind.Cable,
                    0.4 + 0.15 * n, 0.206, 0.122, 90, 300, "NA2XS(F)2Y 3x1x150", []));
                var tr = $"{station.Id}-TR";
                transformers.Add(new(tr, $"{station.Id} 20/0.4 kV compact substation", mv, lv, station.Mva, 20, 0.4, 6,
                    station.Mva >= 1 ? 10.5 : 6.5, station.Mva >= 1 ? 1.5 : 1, false, 0, 0, 0, 0, 0, null, 0, 0, "Dyn11", new(x, y + 1)));
                var load = $"{station.Id}-L";
                loads.Add(new(load, $"{station.Id} consumers", lv, station.PeakMw, 0.95, station.Profile, station.Customers, station.Critical, new(x + 1, y + 2)));
                var ds = $"{station.Id}-DS";
                switches.Add(new(ds, $"{station.Id} MV isolator", GridSwitchKind.Disconnector, mv, tr, true, null, new(x - 1, y + 1)));
                ids.AddRange([lineId, mv, lv, tr, load, ds]);
                previous = mv;
            }
            var qf = $"{feeder}-QF";
            switches.Add(new(qf, $"{feeder} feeder breaker", GridSwitchKind.Breaker, bus, $"{feeder}-C1", true, 25, new(9, y)));
            ids.Add(qf);
            foreach (var (suffix, function, pickup, time, curve) in new (string, GridProtectionFunction, double, double, GridCurveType)[]
            {
                ("51", GridProtectionFunction.Overcurrent51, 160, 0.2, GridCurveType.IecStandardInverse),
                ("50", GridProtectionFunction.InstantaneousOvercurrent50, 2500, 0.05, GridCurveType.DefiniteTime),
                ("51N", GridProtectionFunction.EarthFault51N, 40, 0.3, GridCurveType.DefiniteTime)
            })
            {
                var relay = $"{qf}-{suffix}";
                protections.Add(new(relay, $"{feeder} feeder {suffix}", qf, function, curve, pickup, time, true));
                ids.Add(relay);
            }
            feederRtuIds[index] = ids;
            return (ids, previous);
        }

        var ringA1 = AddFeeder(0, "F1", "MV-A", 2,
        [
            ("SS11", 1.0, 0.82, GridLoadProfile.Commercial, 95, false),
            ("SS12", 1.0, 0.78, GridLoadProfile.Commercial, 80, false),
            ("SS13", 0.63, 0.46, GridLoadProfile.Commercial, 45, false),
            ("SS14", 0.63, 0.5, GridLoadProfile.Residential, 210, false)
        ]);
        var ringA2 = AddFeeder(1, "F2", "MV-A", 6,
        [
            ("SS21", 1.0, 0.85, GridLoadProfile.Commercial, 110, true),
            ("SS22", 0.63, 0.44, GridLoadProfile.Commercial, 40, false),
            ("SS23", 0.63, 0.48, GridLoadProfile.Residential, 190, false)
        ]);
        var ringB1 = AddFeeder(2, "F3", "MV-B", 10,
        [
            ("SS31", 1.0, 0.8, GridLoadProfile.Commercial, 90, false),
            ("SS32", 1.0, 0.75, GridLoadProfile.Commercial, 70, false),
            ("SS33", 0.63, 0.45, GridLoadProfile.Commercial, 42, false),
            ("SS34", 0.63, 0.52, GridLoadProfile.Residential, 230, false)
        ]);
        var ringB2 = AddFeeder(3, "F4", "MV-B", 14,
        [
            ("SS41", 1.0, 0.83, GridLoadProfile.Commercial, 100, false),
            ("SS42", 0.63, 0.47, GridLoadProfile.Commercial, 48, false),
            ("SS43", 0.63, 0.49, GridLoadProfile.Residential, 200, false)
        ]);

        // Normally-open ring points close each pair of feeders into an open ring for restoration after a fault.
        bool AddTie(string tie, string nop, string fromMv, string toMv, int fromX, int fromY, int toX, int toY, int ownerRtu)
        {
            var mid = new DiagramPosition((fromX + toX) / 2.0, (fromY + toY) / 2.0);
            lines.Add(new(tie, $"{tie} ring tie cable", fromMv, toMv, GridLineKind.Cable, 0.4, 0.206, 0.122, 90, 300,
                "NA2XS(F)2Y 3x1x150", [mid]));
            switches.Add(new(nop, $"{tie} normally-open ring point", GridSwitchKind.LoadBreakSwitch, fromMv, tie, false, null, mid));
            feederRtuIds[ownerRtu].AddRange([tie, nop]);
            return true;
        }
        AddTie("TIE-A", "NOP-A", ringA1.LastMv, ringA2.LastMv, 27, 2, 22, 6, 0);
        AddTie("TIE-B", "NOP-B", ringB1.LastMv, ringB2.LastMv, 27, 10, 22, 14, 2);

        var capacitors = new List<GridCapacitorDefinition>
        {
            new("CAP1", "20 kV capacitor bank section A", "MV-A", 2.4, 4, 0, new(7, 2))
        };
        var generators = new List<GridGeneratorDefinition>();

        rtus.Add(new("RTU1", "Primary substation RTU", 1, null,
            ["HV", "MV-A", "MV-B", "GRID", "TR1", "TR2", "DS1", "DS2", "Q0A", "Q0B", "BC", "COUPLER", "CAP1",
                "Q0A-51", "Q0A-50", "MV-A-27", "Q0B-51", "Q0B-50", "MV-B-27", "BC-51"]));
        rtus.Add(new("RTU2", "Ring A feeder F1 RTU", 2, null, feederRtuIds[0]));
        rtus.Add(new("RTU3", "Ring A feeder F2 RTU", 3, null, feederRtuIds[1]));
        rtus.Add(new("RTU4", "Ring B feeder F3 RTU", 4, null, feederRtuIds[2]));
        rtus.Add(new("RTU5", "Ring B feeder F4 RTU", 5, null, feederRtuIds[3]));

        return GridProtectionDefaults.Add(new("Urban 20 kV cable grid (2x40 MVA)", 50, buses,
            [new("GRID", "110 kV upstream grid", "HV", 1, 3000, new(0, 8))], lines, transformers,
            switches, loads, generators, capacitors, protections, rtus));
    }
}
