namespace Domain.Control;

public static class DefaultControllerDefinitions
{
    public static IReadOnlyList<ControllerDefinition> Create() =>
    [
        new("PLC-1", "Transfer Station PLC", ControllerKind.Plc, 500, "L-PLC1-IO", "L-SCADA-PLC1", 4,
            [
                new(0, "LT-101 level x10", "LT-101", IoSignal.Measurement, 10),
                new(1, "LT-102 level x10", "LT-102", IoSignal.Measurement, 10),
                new(2, "FT-101 flow x100", "FT-101", IoSignal.Measurement, 100),
                new(3, "FT-102 flow x100", "FT-102", IoSignal.Measurement, 100),
                new(4, "FT-104 flow x100", "FT-104", IoSignal.Measurement, 100),
                new(5, "PT-101 pressure x100", "PT-101", IoSignal.Measurement, 100),
                new(6, "PT-103 pressure x100", "PT-103", IoSignal.Measurement, 100)
            ],
            [
                new(0, "P-101 run command", "P-101", IoSignal.PumpRunCommand, 1),
                new(1, "P-103 run command", "P-103", IoSignal.PumpRunCommand, 1),
                new(2, "V-101 position x10", "V-101", IoSignal.ValvePositionCommand, 10),
                new(3, "V-102 position x10", "V-102", IoSignal.ValvePositionCommand, 10),
                new(4, "V-103 position x10", "V-103", IoSignal.ValvePositionCommand, 10)
            ],
            [new("T-102 fill", "LT-102", ["P-101", "P-103"], 100, 101, 40, 75, "LT-101", 15)],
            [new("T-101 inlet", "LT-101", "V-101", 102, 103, 45, 80)]),
        new("PLC-2", "Treatment PLC", ControllerKind.Plc, 500, "L-PLC2-IO", "L-SCADA-PLC2", 4,
            [
                new(0, "LT-102 level x10", "LT-102", IoSignal.Measurement, 10),
                new(1, "LT-103 level x10", "LT-103", IoSignal.Measurement, 10),
                new(2, "FT-103 flow x100", "FT-103", IoSignal.Measurement, 100),
                new(3, "PT-102 pressure x100", "PT-102", IoSignal.Measurement, 100)
            ],
            [
                new(0, "P-102 run command", "P-102", IoSignal.PumpRunCommand, 1),
                new(1, "V-201 position x10", "V-201", IoSignal.ValvePositionCommand, 10),
                new(2, "V-302 position x10", "V-302", IoSignal.ValvePositionCommand, 10)
            ],
            [new("T-103 fill", "LT-103", ["P-102"], 100, 101, 45, 80, "LT-102", 25)],
            []),
        new("RTU-1", "Reservoir RTU", ControllerKind.Rtu, 2000, "L-RTU1-IO", "L-SCADA-RTU1", 3,
            [
                new(0, "LT-103 level x10", "LT-103", IoSignal.Measurement, 10),
                new(1, "FT-301 flow x100", "FT-301", IoSignal.Measurement, 100)
            ],
            [new(0, "V-301 position x10", "V-301", IoSignal.ValvePositionCommand, 10)],
            [],
            [])
    ];
}
