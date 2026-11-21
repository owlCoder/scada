using Domain.Process.Snapshots;

namespace Hmi.Services;

/// <summary>
/// Positions for the SVG plant diagram, derived from the snapshot so a custom topology still draws: tanks in a row, pumps
/// below between their source and target tanks, inlet valves above and outlet/drain valves below their tank.
/// </summary>
public sealed class PlantLayout
{
    public const double TankWidth = 120;
    public const double TankHeight = 170;
    public const double TankTop = 90;
    public const double PumpY = 360;
    public const double InletY = 40;
    public const double OutletY = 300;
    private const double Spacing = 260;
    private const double Margin = 70;

    private readonly Dictionary<string, (double X, double Y)> _positions = new(StringComparer.Ordinal);

    public PlantLayout(PlantSnapshot plant)
    {
        ArgumentNullException.ThrowIfNull(plant);
        for (var i = 0; i < plant.Tanks.Count; i++)
            _positions[plant.Tanks[i].Id] = (Margin + i * Spacing, TankTop);
        Width = Margin * 2 + Math.Max(1, plant.Tanks.Count) * Spacing - (Spacing - TankWidth);

        var pumpsPerPair = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pump in plant.Pumps)
        {
            var from = TankCenter(pump.SourceTankId);
            var to = TankCenter(pump.TargetTankId);
            var pair = pump.SourceTankId + ">" + pump.TargetTankId;
            var index = pumpsPerPair[pair] = pumpsPerPair.GetValueOrDefault(pair) + 1;
            _positions[pump.Id] = ((from + to) / 2, PumpY + (index - 1) * 60);
        }

        var valvesPerTank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var valve in plant.Valves)
        {
            if (valve.Role == Domain.Process.Devices.ValveRole.Discharge)
            {
                var pump = plant.Pumps.FirstOrDefault(p => p.DischargeValveId == valve.Id);
                _positions[valve.Id] = pump is null ? (Margin, OutletY) : (Position(pump.Id).X + 50, Position(pump.Id).Y);
                continue;
            }

            var tank = valve.TankId ?? "";
            var index = valvesPerTank[tank + valve.Role] = valvesPerTank.GetValueOrDefault(tank + valve.Role) + 1;
            var x = (_positions.TryGetValue(tank, out var t) ? t.X : Margin) + TankWidth / 2 + (index - 1) * 36;
            _positions[valve.Id] = (x, valve.Role == Domain.Process.Devices.ValveRole.Inlet ? InletY : OutletY);
        }

        Height = Math.Max(PumpY + 80, _positions.Values.Select(p => p.Y).DefaultIfEmpty(0).Max() + 60);
    }

    public double Width { get; }
    public double Height { get; }

    public (double X, double Y) Position(string id) => _positions.GetValueOrDefault(id, (Margin, Margin));

    public double TankCenter(string tankId) => Position(tankId).X + TankWidth / 2;

    public static string Number(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
