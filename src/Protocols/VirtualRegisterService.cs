using Application;

namespace Protocols;

public sealed record RegisterDefinition(ushort Address, string Name, bool Writable, double Scale);

public sealed record RegisterValue(ushort Address, string Name, ushort Value, bool Writable);

public sealed class VirtualRegisterService(IPlantService plant)
{
    private static readonly RegisterDefinition[] Definitions =
    [
        new(0, "Ниво резервоара", false, 10),
        new(1, "Стање пумпе", true, 1),
        new(2, "Стање вентила", true, 1),
        new(3, "Улазни проток", false, 100),
        new(4, "Излазни проток", false, 100)
    ];

    public IReadOnlyList<RegisterValue> ReadAll()
    {
        var state = plant.GetState();
        return
        [
            new(0, Definitions[0].Name, checked((ushort)Math.Round(state.TankLevel * 10)), false),
            new(1, Definitions[1].Name, (ushort)(state.PumpRunning ? 1 : 0), true),
            new(2, Definitions[2].Name, (ushort)(state.OutletValveOpen ? 1 : 0), true),
            new(3, Definitions[3].Name, checked((ushort)Math.Round(state.InflowRate * 100)), false),
            new(4, Definitions[4].Name, checked((ushort)Math.Round(state.OutflowRate * 100)), false)
        ];
    }

    public RegisterValue? Read(ushort address) =>
        ReadAll().FirstOrDefault(register => register.Address == address);

    public bool Write(ushort address, ushort value, string actor)
    {
        if (value > 1)
            return false;

        switch (address)
        {
            case 1:
                plant.SetPump(value == 1, actor);
                return true;
            case 2:
                plant.SetOutletValve(value == 1, actor);
                return true;
            default:
                return false;
        }
    }
}
