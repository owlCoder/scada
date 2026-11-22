using Domain.Process.Snapshots;

namespace Hmi.Services;

/// <summary>
/// Positions for the SVG P&ID workspace, derived from the snapshot so a custom topology still draws: tanks in a row,
/// pumps below between their source and target tanks, inlet valves above and outlet/drain valves below their tank,
/// ISA instrument bubbles in fixed slots next to the device they measure, and controller station boxes in a row at the
/// bottom. <see cref="FlowArrows"/> places direction arrows on process lines.
/// </summary>
public sealed class PlantLayout
{
    public const double TankWidth = 120;
    public const double TankHeight = 170;
    public const double TankTop = 90;
    public const double PumpY = 360;
    public const double InletY = 40;
    public const double OutletY = 300;
    public const double BubbleRadius = 16;
    public const double ControllerBoxWidth = 190;
    public const double ControllerBoxHeight = 62;
    private const double Spacing = 260;
    private const double Margin = 70;

    private readonly Dictionary<string, (double X, double Y)> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y, double Cx, double Cy)> _sensorSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y)> _controllerPositions = new(StringComparer.Ordinal);

    public PlantLayout(PlantSnapshot plant, IReadOnlyList<string>? controllerIds = null)
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

        // ISA instrument bubbles: one fixed slot per sensor, next to the device it measures, so labels never overlap.
        var sensorsPerDevice = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sensor in plant.Sensors)
        {
            var index = sensorsPerDevice[sensor.DeviceId] = sensorsPerDevice.GetValueOrDefault(sensor.DeviceId) + 1;
            var device = Position(sensor.DeviceId);
            _sensorSlots[sensor.Id] = plant.Tank(sensor.DeviceId) is not null
                ? (device.X + TankWidth + 38, device.Y + TankHeight / 2, device.X + TankWidth, device.Y + TankHeight / 2)
                : plant.Pump(sensor.DeviceId) is not null
                    ? index == 1
                        ? (device.X - 66, device.Y + 46, device.X - 14, device.Y + 14)
                        : (device.X + 66, device.Y + 46, device.X + 14, device.Y + 14)
                    : (device.X + 58, device.Y - 6, device.X + 14, device.Y - 4);
        }

        var contentBottom = Math.Max(PumpY + 80, _positions.Values.Select(p => p.Y).DefaultIfEmpty(0).Max() + 60);
        var controllerY = contentBottom + 16;
        if (controllerIds is not null)
            for (var i = 0; i < controllerIds.Count; i++)
                _controllerPositions[controllerIds[i]] = (Margin + i * (ControllerBoxWidth + 30), controllerY);

        Height = controllerIds is { Count: > 0 }
            ? controllerY + ControllerBoxHeight + 30
            : contentBottom;
        Width = Math.Max(Width, _sensorSlots.Values.Select(s => s.X).DefaultIfEmpty(0).Max() + 90);
        if (controllerIds is { Count: > 0 })
            Width = Math.Max(Width, Margin * 2 + controllerIds.Count * (ControllerBoxWidth + 30) - 30);
    }

    public double Width { get; }
    public double Height { get; }

    public (double X, double Y) Position(string id) => _positions.GetValueOrDefault(id, (Margin, Margin));

    /// <summary>Any selectable element's anchor point: device position, instrument bubble or controller box.</summary>
    public (double X, double Y) Anchor(string id)
    {
        if (_positions.TryGetValue(id, out var position))
            return position;
        if (_sensorSlots.TryGetValue(id, out var slot))
            return (slot.X, slot.Y);
        return _controllerPositions.GetValueOrDefault(id, (Margin, Margin));
    }

    public double TankCenter(string tankId) => Position(tankId).X + TankWidth / 2;

    /// <summary>Bubble centre and the process-point end of the dashed connector line for one instrument.</summary>
    public (double X, double Y, double Cx, double Cy) SensorSlot(string sensorId) =>
        _sensorSlots.GetValueOrDefault(sensorId, (Margin, Margin, Margin, Margin));

    public (double X, double Y) ControllerPosition(string controllerId) =>
        _controllerPositions.GetValueOrDefault(controllerId, (Margin, Height - ControllerBoxHeight));

    /// <summary>Direction arrows along a polyline, one per segment longer than <paramref name="minSegmentLength"/>.</summary>
    public static IReadOnlyList<(double X, double Y, double Degrees)> FlowArrows(
        IReadOnlyList<(double X, double Y)> points, double minSegmentLength = 70, int maxArrows = 3)
    {
        ArgumentNullException.ThrowIfNull(points);
        var arrows = new List<(double X, double Y, double Degrees)>();
        for (var i = 0; i + 1 < points.Count && arrows.Count < maxArrows; i++)
        {
            var (x1, y1) = points[i];
            var (x2, y2) = points[i + 1];
            var length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (length < minSegmentLength)
                continue;
            var degrees = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
            arrows.Add(((x1 + x2) / 2, (y1 + y2) / 2, degrees));
        }
        return arrows;
    }

    public static string Number(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
