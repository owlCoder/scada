using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Hmi.Services;

/// <summary>
/// Positions for the SVG P&ID workspace, derived from the snapshot so a custom topology still draws: tanks spread in a
/// row across the available width (targetWidth, when the canvas size is known), pumps in rows below between their
/// source and target tanks (parallel rows far enough apart for label + value), inlet valves above and
/// outlet/drain valves below their tank, ISA instrument bubbles in fixed slots next to the device they measure, and
/// controller station boxes in a row at the bottom. <see cref="PlaceLabels"/> runs the shared
/// <see cref="LabelPlacer"/> over label candidates against <see cref="SymbolObstacles"/>, so every symbol's label
/// gets a fixed slot with collision avoidance. <see cref="FlowArrows"/> places direction arrows on process lines.
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

    /// <summary>Vertical distance between parallel pump rows (P-101/P-103): room for each row's label and value text.</summary>
    public const double PumpRowSpacing = 112;

    /// <summary>Tank spacing bounds when spreading across a target width.</summary>
    public const double MinTankSpacing = 260;
    public const double MaxTankSpacing = 680;

    /// <summary>How far (SVG units) a label may travel from its preferred slot before it is dropped.</summary>
    public const double MaxLabelSearch = 170;

    private const double Margin = 70;
    private const double ObstaclePad = 4;

    private readonly Dictionary<string, (double X, double Y)> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y, double Cx, double Cy)> _sensorSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y)> _controllerPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _kinds = new(StringComparer.Ordinal);
    private readonly PlantSnapshot _plant;

    public PlantLayout(PlantSnapshot plant, IReadOnlyList<string>? controllerIds = null, double? targetWidth = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        _plant = plant;
        var spacing = MinTankSpacing;
        if (targetWidth is > 0 && plant.Tanks.Count > 1)
            spacing = Math.Clamp((targetWidth.Value - 2 * Margin - TankWidth) / (plant.Tanks.Count - 1), MinTankSpacing, MaxTankSpacing);
        for (var i = 0; i < plant.Tanks.Count; i++)
        {
            _positions[plant.Tanks[i].Id] = (Margin + i * spacing, TankTop);
            _kinds[plant.Tanks[i].Id] = "tank";
        }
        Width = Margin * 2 + Math.Max(1, plant.Tanks.Count) * spacing - (spacing - TankWidth);

        var pumpsPerPair = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pump in plant.Pumps)
        {
            var from = TankCenter(pump.SourceTankId);
            var to = TankCenter(pump.TargetTankId);
            var pair = pump.SourceTankId + ">" + pump.TargetTankId;
            var index = pumpsPerPair[pair] = pumpsPerPair.GetValueOrDefault(pair) + 1;
            _positions[pump.Id] = ((from + to) / 2, PumpY + (index - 1) * PumpRowSpacing);
            _kinds[pump.Id] = "pump";
        }

        var valvesPerTank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var valve in plant.Valves)
        {
            if (valve.Role == Domain.Process.Devices.ValveRole.Discharge)
            {
                var pump = plant.Pumps.FirstOrDefault(p => p.DischargeValveId == valve.Id);
                _positions[valve.Id] = pump is null ? (Margin, OutletY) : (Position(pump.Id).X + 50, Position(pump.Id).Y);
                _kinds[valve.Id] = "valve";
                continue;
            }

            var tank = valve.TankId ?? "";
            var index = valvesPerTank[tank + valve.Role] = valvesPerTank.GetValueOrDefault(tank + valve.Role) + 1;
            var x = (_positions.TryGetValue(tank, out var t) ? t.X : Margin) + TankWidth / 2 + (index - 1) * 36;
            _positions[valve.Id] = (x, valve.Role == Domain.Process.Devices.ValveRole.Inlet ? InletY : OutletY);
            _kinds[valve.Id] = "valve";
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
            _kinds[sensor.Id] = "sensor";
        }

        var contentBottom = Math.Max(PumpY + 80, _positions.Values.Select(p => p.Y).DefaultIfEmpty(0).Max() + 60);
        var controllerY = contentBottom + 16;
        if (controllerIds is not null)
            for (var i = 0; i < controllerIds.Count; i++)
            {
                _controllerPositions[controllerIds[i]] = (Margin + i * (ControllerBoxWidth + 30), controllerY);
                _kinds[controllerIds[i]] = "controller";
            }

        Height = controllerIds is { Count: > 0 }
            ? controllerY + ControllerBoxHeight + 30
            : contentBottom;
        Width = Math.Max(Width, _sensorSlots.Values.Select(s => s.X).DefaultIfEmpty(0).Max() + 90);
        if (controllerIds is { Count: > 0 })
            Width = Math.Max(Width, Margin * 2 + controllerIds.Count * (ControllerBoxWidth + 30) - 30);

        AlarmBells = BuildAlarmBells(plant);
    }

    public double Width { get; }
    public double Height { get; }

    /// <summary>Active alarm bells with their fixed slots (rendered above the device they belong to).</summary>
    public IReadOnlyList<PidAlarmBell> AlarmBells { get; }

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

    /// <summary>Label-box size for a text (tabular value font, 7 px per char plus padding).</summary>
    public static (double Width, double Height) LabelBoxSize(string text) => ((text?.Length ?? 0) * 7 + 8, 16);

    /// <summary>
    /// Obstacle rectangles of every drawn symbol (tanks, pumps, valves, instrument bubbles, controller boxes) and
    /// alarm bell, so placed labels never cover a symbol or a bell.
    /// </summary>
    public IReadOnlyList<LabelObstacle> SymbolObstacles()
    {
        var obstacles = new List<LabelObstacle>();
        foreach (var (id, position) in _positions)
        {
            var (w, h) = _kinds.GetValueOrDefault(id) switch
            {
                "tank" => (TankWidth, TankHeight),
                "pump" => (44, 44),
                _ => (36, 42) // valves: bow-tie plus stem
            };
            obstacles.Add(LabelObstacle.Centered(position.X, position.Y, w + 2 * ObstaclePad, h + 2 * ObstaclePad));
        }
        foreach (var slot in _sensorSlots.Values)
            obstacles.Add(LabelObstacle.Centered(slot.X, slot.Y, 2 * BubbleRadius + 2 * ObstaclePad, 2 * BubbleRadius + 2 * ObstaclePad));
        foreach (var controller in _controllerPositions.Values)
            obstacles.Add(LabelObstacle.Centered(controller.X + ControllerBoxWidth / 2, controller.Y + ControllerBoxHeight / 2,
                ControllerBoxWidth + 2 * ObstaclePad, ControllerBoxHeight + 2 * ObstaclePad));
        foreach (var bell in AlarmBells)
            obstacles.Add(LabelObstacle.Centered(bell.X, bell.Y, 22, 20));
        return obstacles;
    }

    /// <summary>
    /// Places label boxes collision-free against the symbol obstacles: each candidate first tries its fixed preferred
    /// slot, then offset rings; when no free slot exists within <see cref="MaxLabelSearch"/> the lowest-priority
    /// label is dropped instead of overlapping.
    /// </summary>
    public IReadOnlyDictionary<string, LabelPlacement> PlaceLabels(IEnumerable<PidLabel> labels) =>
        LabelPlacer.Place(labels.Select(l => l.ToCandidate()).ToArray(), SymbolObstacles(), 1, MaxLabelSearch)
            .ToDictionary(p => p.Id);

    /// <summary>All labels of the current detail level with their fixed preferred slots.</summary>
    public IReadOnlyList<PidLabel> Labels(PidDetail detail)
    {
        var list = new List<PidLabel>();
        foreach (var tank in _plant.Tanks)
        {
            var (tx, ty) = Position(tank.Id);
            list.Add(Make(tank.Id, tank.Id, "pid-device-label", tx + TankWidth / 2, ty - 14, 10));
            if (detail == PidDetail.Full && Quality(tank.Id, SensorKind.Level) is { } levelQuality && levelQuality != MeasurementQuality.Good)
                list.Add(Make(tank.Id + "#q", $"quality: {levelQuality}", "pid-quality", tx + TankWidth / 2, ty + TankHeight + 14, 15));
        }
        foreach (var pump in _plant.Pumps)
        {
            var (px, py) = Position(pump.Id);
            list.Add(Make(pump.Id, pump.Id, "pid-device-label", px, py - 36, 10));
            if (detail != PidDetail.Symbols)
                list.Add(Make(pump.Id + "#v", pump.FlowRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m³/min", "pid-label-small", px, py + 44, 20));
            if (detail == PidDetail.Full && Quality(pump.Id, SensorKind.Flow) is { } flowQuality && flowQuality != MeasurementQuality.Good)
                list.Add(Make(pump.Id + "#q", $"quality: {flowQuality}", "pid-quality", px, py + 62, 15));
        }
        foreach (var valve in _plant.Valves)
        {
            var (vx, vy) = Position(valve.Id);
            list.Add(Make(valve.Id, valve.Id + " " + valve.Position.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%", "pid-device-label", vx, vy - 28, 10));
            if (detail == PidDetail.Full && Quality(valve.Id, SensorKind.Flow) is { } valveQuality && valveQuality != MeasurementQuality.Good)
                list.Add(Make(valve.Id + "#q", $"quality: {valveQuality}", "pid-quality", vx, vy + 30, 15));
        }
        if (detail != PidDetail.Symbols)
        {
            foreach (var sensor in _plant.Sensors)
            {
                var (bx, by, cx, _) = SensorSlot(sensor.Id);
                var valueText = sensor.ReportedValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + sensor.Unit;
                // Beside the bubble, on the side opposite the dashed connector line.
                var side = cx >= bx ? -1 : 1;
                var (width, _) = LabelBoxSize(valueText);
                list.Add(Make(sensor.Id + "#v", valueText, "pid-bubble-value", bx + side * (BubbleRadius + 6 + width / 2), by + 4, 20));
                if (detail == PidDetail.Full && sensor.Quality != MeasurementQuality.Good)
                {
                    var qualityText = sensor.Quality.ToString();
                    var (qWidth, _) = LabelBoxSize(qualityText);
                    list.Add(Make(sensor.Id + "#q", qualityText, "pid-quality", bx + side * (BubbleRadius + 6 + qWidth / 2), by + 20, 15));
                }
            }
        }
        return list;
    }

    private static PidLabel Make(string id, string text, string css, double centerX, double centerY, int priority)
    {
        var (width, height) = LabelBoxSize(text);
        return new PidLabel(id, text, css, centerX, centerY, width, height, priority);
    }

    private MeasurementQuality? Quality(string deviceId, SensorKind kind) =>
        _plant.Sensors.FirstOrDefault(s => s.DeviceId == deviceId && s.Kind == kind)?.Quality;

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

    private IReadOnlyList<PidAlarmBell> BuildAlarmBells(PlantSnapshot plant)
    {
        var bells = new List<PidAlarmBell>();
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var alarm in plant.Alarms.Where(a => a.IsActive))
        {
            var index = slots[alarm.SourceId] = slots.GetValueOrDefault(alarm.SourceId) + 1;
            double bx, by;
            if (plant.Tank(alarm.SourceId) is { } tank)
            {
                var (tx, ty) = Position(tank.Id);
                (bx, by) = (tx + TankWidth / 2 + (index - 1) * 22, ty - 26);
            }
            else if (plant.Sensor(alarm.SourceId) is not null)
            {
                var (sx, sy, _, _) = SensorSlot(alarm.SourceId);
                (bx, by) = (sx + (index - 1) * 22, sy - 26);
            }
            else if (_controllerPositions.TryGetValue(alarm.SourceId, out var controller))
            {
                (bx, by) = (controller.X + ControllerBoxWidth / 2 + (index - 1) * 22, controller.Y - 12);
            }
            else
            {
                var device = Position(alarm.SourceId);
                (bx, by) = (device.X + (index - 1) * 22, device.Y - 56);
            }
            bells.Add(new PidAlarmBell(alarm.SourceId, bx, by, alarm.Severity, alarm.IsAcknowledged, alarm.Message));
        }
        return bells;
    }
}
