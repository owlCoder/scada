using Domain.Grid.Definition;

namespace UnitTests.Support;

public static class GridEngineNetworks
{
    public static GridDefinition TwoBus() => new("Hand calculation", 50,
        [new("A", "Infeed", 20, GridBusKind.Busbar, new(0, 0)), new("B", "Load bus", 20, GridBusKind.ConsumerPoint, new(2, 0))],
        [new("S", "Stiff source", "A", 1, 1e9, new(0, 0))],
        [new("L", "Line", "A", "B", GridLineKind.Cable, 2, 0.2, 0.1, 0, 300, null, [])], [],
        [new("Q", "Breaker", GridSwitchKind.Breaker, "A", "L", true, 25, new(1, 0))],
        [new("LOAD", "Load", "B", 1, 0.95, GridLoadProfile.Constant, 200, false, new(3, 0))], [], [], [],
        [new("R", "RTU", 1, null, ["A", "B", "L", "Q", "LOAD"])]);
}
