using Domain.Grid.Definition;

namespace UnitTests.Security.Grid.Support;

/// <summary>
/// Two different scripted distribution networks used to prove that grid scenarios resolve their targets generically.
/// Default: a 10 kV training feeder with a 10/0.4 kV OLTC transformer, four line feeders, eight loads, two capacitor
/// banks, four feeder protections and two RTUs. Alternative: a 20 kV two-feeder network without an OLTC and with one
/// capacitor bank, used to test target resolution and clear start failures.
/// </summary>
public static class SecurityFakeGridNetworks
{
    public static GridDefinition Default() => new(
        "Training feeder 10 kV",
        50.0,
        Buses:
        [
            Bus("BUS-01", "Main 10 kV bus", 10, GridBusKind.Busbar, 0, 0),
            Bus("BUS-LV", "LV board 0.4 kV", 0.4, GridBusKind.ConsumerPoint, 4, 0),
            Bus("BUS-F1", "Feeder 1", 10, GridBusKind.ConsumerPoint, 0, 4),
            Bus("BUS-F2", "Feeder 2", 10, GridBusKind.ConsumerPoint, 4, 4),
            Bus("BUS-F3", "Feeder 3", 10, GridBusKind.ConsumerPoint, 8, 4),
            Bus("BUS-F4", "Feeder 4", 10, GridBusKind.ConsumerPoint, 12, 4)
        ],
        Sources: [new GridSourceDefinition("SRC-01", "Upstream grid", "BUS-01", 1.0, 500, new DiagramPosition(0, 2))],
        Lines:
        [
            Line("L-01", "Feeder 1 line", "BUS-01", "BUS-F1", 250),
            Line("L-02", "Feeder 2 line", "BUS-01", "BUS-F2", 300),
            Line("L-03", "Feeder 3 line", "BUS-01", "BUS-F3", 300),
            Line("L-04", "Feeder 4 line", "BUS-01", "BUS-F4", 300)
        ],
        Transformers:
        [
            new GridTransformerDefinition("TR-01", "10/0.4 kV transformer", "BUS-01", "BUS-LV", 2.5, 10, 0.4, 6, 20, 2,
                HasOnLoadTapChanger: true, TapMin: -8, TapMax: 8, TapNeutral: 0, InitialTap: 0, TapStepPercent: 1.5,
                AvrTargetLvKv: 0.4, AvrDeadbandPercent: 2, AvrDelaySeconds: 30, CatalogType: null, new DiagramPosition(2, 0))
        ],
        Switches:
        [
            Switch("SW-L01", "Feeder 1 breaker", "BUS-01", "L-01"),
            Switch("SW-L02", "Feeder 2 breaker", "BUS-01", "L-02"),
            Switch("SW-L03", "Feeder 3 breaker", "BUS-01", "L-03"),
            Switch("SW-L04", "Feeder 4 breaker", "BUS-01", "L-04"),
            Switch("SW-TR", "Transformer breaker", "BUS-01", "TR-01")
        ],
        Loads:
        [
            Load("LD-F1A", "Feeder 1 plant", "BUS-F1", 2.5, 600),
            Load("LD-F1B", "Feeder 1 town", "BUS-F1", 1.5, 400),
            Load("LD-F2A", "Feeder 2 town", "BUS-F2", 2.0, 500),
            Load("LD-F2B", "Feeder 2 farm", "BUS-F2", 0.8, 100),
            Load("LD-F3A", "Feeder 3 town", "BUS-F3", 1.8, 450),
            Load("LD-F3B", "Feeder 3 shop", "BUS-F3", 0.7, 50),
            Load("LD-F4A", "Feeder 4 town", "BUS-F4", 1.6, 350),
            Load("LD-LV", "LV board consumers", "BUS-LV", 2.0, 800)
        ],
        Generators: [new GridGeneratorDefinition("PV-01", "Rooftop PV", "BUS-F3", GridGeneratorKind.Photovoltaic, 0.8, new DiagramPosition(8, 6))],
        Capacitors:
        [
            new GridCapacitorDefinition("CAP-LV", "LV capacitor bank", "BUS-LV", 0.6, 4, 0, new DiagramPosition(5, 0)),
            new GridCapacitorDefinition("CAP-F2", "Feeder 2 capacitor", "BUS-F2", 0.3, 2, 0, new DiagramPosition(4, 5))
        ],
        Protections:
        [
            Protection("PR-L01", "SW-L01", 300),
            Protection("PR-L02", "SW-L02", 300),
            Protection("PR-L03", "SW-L03", 300),
            Protection("PR-L04", "SW-L04", 300),
            Protection("PR-TR", "SW-TR", 400)
        ],
        Rtus:
        [
            new GridRtuDefinition("RTU-1", "Infeed RTU", 1, "LINK-1",
                ["SRC-01", "TR-01", "SW-TR", "L-01", "SW-L01", "L-02", "SW-L02", "BUS-01", "BUS-LV", "CAP-LV", "LD-LV"]),
            new GridRtuDefinition("RTU-2", "Feeder RTU", 2, "LINK-2",
                ["L-03", "SW-L03", "L-04", "SW-L04", "BUS-F1", "BUS-F2", "BUS-F3", "BUS-F4", "LD-F1A", "LD-F1B", "LD-F2A", "LD-F2B",
                    "LD-F3A", "LD-F3B", "LD-F4A", "PV-01", "CAP-F2"])
        ]);

    public static GridDefinition Alternative() => new(
        "Alternative 20 kV radial",
        50.0,
        Buses:
        [
            Bus("N1", "Infeed 20 kV", 20, GridBusKind.Busbar, 0, 0),
            Bus("N2", "Feeder A", 20, GridBusKind.ConsumerPoint, 4, 0),
            Bus("N3", "Feeder B", 20, GridBusKind.ConsumerPoint, 8, 0),
            Bus("N4", "Feeder C", 20, GridBusKind.ConsumerPoint, 12, 0)
        ],
        Sources: [new GridSourceDefinition("GRID", "Upstream 20 kV", "N1", 1.05, 800, new DiagramPosition(0, 2))],
        Lines:
        [
            Line("LINE-A", "Feeder A line", "N1", "N2", 400),
            Line("LINE-B", "Feeder B line", "N1", "N3", 400),
            Line("LINE-C", "Feeder C line", "N1", "N4", 400)
        ],
        Transformers: [],
        Switches:
        [
            Switch("CB-A", "Feeder A breaker", "N1", "LINE-A"),
            Switch("CB-B", "Feeder B breaker", "N1", "LINE-B"),
            Switch("CB-C", "Feeder C breaker", "N1", "LINE-C")
        ],
        Loads:
        [
            Load("LOAD-1", "Feeder A town", "N2", 3.0, 700),
            Load("LOAD-2", "Feeder B town", "N3", 2.0, 500),
            Load("LOAD-3", "Feeder B farm", "N3", 1.0, 200),
            Load("LOAD-4", "Feeder C shop", "N4", 1.0, 150)
        ],
        Generators: [],
        Capacitors: [new GridCapacitorDefinition("CAP-B", "Feeder B capacitor", "N3", 0.5, 3, 0, new DiagramPosition(9, 1))],
        Protections:
        [
            Protection("REL-A", "CB-A", 500),
            Protection("REL-B", "CB-B", 500),
            Protection("REL-C", "CB-C", 500)
        ],
        Rtus:
        [
            new GridRtuDefinition("RTU-A", "Infeed RTU", 10, "LINK-A", ["GRID", "LINE-A", "CB-A", "LINE-C", "CB-C", "N1", "N2", "N4", "LOAD-1", "LOAD-4"]),
            new GridRtuDefinition("RTU-B", "Feeder B RTU", 11, "LINK-B", ["LINE-B", "CB-B", "N3", "LOAD-2", "LOAD-3", "CAP-B"])
        ]);

    private static GridBusDefinition Bus(string id, string name, double kv, GridBusKind kind, double x, double y) =>
        new(id, name, kv, kind, new DiagramPosition(x, y));

    private static GridLineDefinition Line(string id, string name, string from, string to, double ratedA) =>
        new(id, name, from, to, GridLineKind.Cable, 2.0, 0.1, 0.08, 3.0, ratedA, null, []);

    private static GridSwitchDefinition Switch(string id, string name, string bus, string element) =>
        new(id, name, GridSwitchKind.Breaker, bus, element, NormallyClosed: true, BreakingCapacityKa: 25, new DiagramPosition(1, 1));

    private static GridLoadDefinition Load(string id, string name, string bus, double peakMw, int customers) =>
        new(id, name, bus, peakMw, 0.95, GridLoadProfile.Residential, customers, Critical: false, new DiagramPosition(2, 2));

    private static GridProtectionDefinition Protection(string id, string switchId, double pickupA) =>
        new(id, $"Relay {id}", switchId, GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, pickupA, 0.3, Enabled: true);
}
