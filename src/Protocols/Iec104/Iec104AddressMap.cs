using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Protocols.Iec104;

/// <summary>
/// Deterministic address map built from <see cref="GridDefinition"/>. Per RTU (common address) the IOA ranges are:
/// 1000+ measurements, 2000+ switch status double points, 3000+ transformer tap positions, 4000+ protection trip
/// single points, 5000+ RTU status. Elements listed in <see cref="GridRtuDefinition.ElementIds"/> are mapped in
/// definition order; unknown element ids are skipped.
/// </summary>
public sealed class Iec104AddressMap
{
    public const int MeasurementIoaBase = 1000;
    public const int SwitchIoaBase = 2000;
    public const int TapIoaBase = 3000;
    public const int ProtectionIoaBase = 4000;
    public const int RtuStatusIoaBase = 5000;

    private readonly Dictionary<int, Iec104RtuMap> _byCommonAddress;
    private readonly Dictionary<(int CommonAddress, int Ioa), Iec104Point> _byIoa;

    public Iec104AddressMap(IReadOnlyList<Iec104RtuMap> rtus)
    {
        Rtus = rtus;
        _byCommonAddress = rtus.ToDictionary(r => r.CommonAddress);
        _byIoa = rtus.SelectMany(r => r.Points).ToDictionary(p => (p.CommonAddress, p.InformationObjectAddress));
    }

    public IReadOnlyList<Iec104RtuMap> Rtus { get; }

    public Iec104RtuMap? Rtu(int commonAddress) => _byCommonAddress.GetValueOrDefault(commonAddress);

    public Iec104Point? Point(int commonAddress, int informationObjectAddress) =>
        _byIoa.GetValueOrDefault((commonAddress, informationObjectAddress));

    public static Iec104AddressMap Build(GridDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var buses = definition.Buses.ToDictionary(b => b.Id);
        var lines = definition.Lines.ToDictionary(l => l.Id);
        var transformers = definition.Transformers.ToDictionary(t => t.Id);
        var switches = definition.Switches.ToDictionary(s => s.Id);
        var loads = definition.Loads.ToDictionary(l => l.Id);
        var generators = definition.Generators.ToDictionary(g => g.Id);
        var capacitors = definition.Capacitors.ToDictionary(c => c.Id);
        var protections = definition.Protections.ToDictionary(p => p.Id);
        var rtus = new List<Iec104RtuMap>();
        foreach (var rtu in definition.Rtus)
        {
            if (rtu.CommonAddress is < 1 or > AsduHeader.MaxCommonAddress)
                throw new InvalidOperationException($"RTU {rtu.Id} has an invalid common address {rtu.CommonAddress}.");
            if (rtus.Any(r => r.CommonAddress == rtu.CommonAddress))
                throw new InvalidOperationException($"Duplicate RTU common address {rtu.CommonAddress}.");

            var measurements = new List<Iec104Point>();
            var switchPoints = new List<Iec104Point>();
            var tapPoints = new List<Iec104Point>();
            var protectionPoints = new List<Iec104Point>();
            var occurrence = new Dictionary<(string Element, GridMeasurementKind Kind), int>();
            foreach (var elementId in rtu.ElementIds)
            {
                if (buses.TryGetValue(elementId, out var bus))
                    AddMeasurement(measurements, occurrence, rtu, elementId, bus.Name, GridMeasurementKind.Voltage, bus.NominalVoltageKv);
                else if (lines.TryGetValue(elementId, out var line))
                {
                    var powerRange = PowerRangeMva(line.RatedCurrentA, buses.GetValueOrDefault(line.FromBusId)?.NominalVoltageKv ?? 0);
                    AddMeasurement(measurements, occurrence, rtu, elementId, line.Name, GridMeasurementKind.Current, line.RatedCurrentA);
                    AddMeasurement(measurements, occurrence, rtu, elementId, line.Name, GridMeasurementKind.ActivePower, powerRange);
                    AddMeasurement(measurements, occurrence, rtu, elementId, line.Name, GridMeasurementKind.ReactivePower, powerRange);
                }
                else if (transformers.TryGetValue(elementId, out var transformer))
                {
                    var ratedCurrent = transformer.RatedPowerMva * 1_000_000 / (Math.Sqrt(3) * transformer.HvRatedKv);
                    AddMeasurement(measurements, occurrence, rtu, elementId, transformer.Name, GridMeasurementKind.Current, ratedCurrent);
                    AddMeasurement(measurements, occurrence, rtu, elementId, transformer.Name, GridMeasurementKind.ActivePower, transformer.RatedPowerMva);
                    AddMeasurement(measurements, occurrence, rtu, elementId, transformer.Name, GridMeasurementKind.ReactivePower, transformer.RatedPowerMva);
                    if (transformer.HasOnLoadTapChanger)
                        tapPoints.Add(TapPoint(rtu, elementId, transformer.Name));
                }
                else if (switches.TryGetValue(elementId, out var switchDefinition))
                    switchPoints.Add(SwitchPoint(rtu, elementId, switchDefinition.Name));
                else if (loads.TryGetValue(elementId, out var load))
                {
                    AddMeasurement(measurements, occurrence, rtu, elementId, load.Name, GridMeasurementKind.ActivePower, load.PeakActivePowerMw);
                    AddMeasurement(measurements, occurrence, rtu, elementId, load.Name, GridMeasurementKind.ReactivePower, load.PeakActivePowerMw);
                }
                else if (generators.TryGetValue(elementId, out var generator))
                {
                    AddMeasurement(measurements, occurrence, rtu, elementId, generator.Name, GridMeasurementKind.ActivePower, generator.RatedPowerMw);
                    AddMeasurement(measurements, occurrence, rtu, elementId, generator.Name, GridMeasurementKind.ReactivePower, generator.RatedPowerMw);
                }
                else if (capacitors.TryGetValue(elementId, out var capacitor))
                    AddMeasurement(measurements, occurrence, rtu, elementId, capacitor.Name, GridMeasurementKind.ReactivePower, capacitor.RatedPowerMvar);
                else if (protections.TryGetValue(elementId, out var protection))
                    protectionPoints.Add(ProtectionPoint(rtu, elementId, protection.Name));
                // Unknown element ids are skipped on purpose: the map stays deterministic from the definition.
            }

            var points = new List<Iec104Point>(measurements.Count + switchPoints.Count + tapPoints.Count + protectionPoints.Count + 1);
            points.AddRange(AssignIoa(measurements, MeasurementIoaBase));
            points.AddRange(AssignIoa(switchPoints, SwitchIoaBase));
            points.AddRange(AssignIoa(tapPoints, TapIoaBase));
            points.AddRange(AssignIoa(protectionPoints, ProtectionIoaBase));
            points.Add(AssignIoa([RtuStatusPoint(rtu)], RtuStatusIoaBase)[0]);
            rtus.Add(new Iec104RtuMap(rtu.CommonAddress, rtu.Id, rtu.Name, points));
        }

        return new Iec104AddressMap(rtus);
    }

    private static double PowerRangeMva(double ratedCurrentA, double nominalVoltageKv) =>
        ratedCurrentA * nominalVoltageKv * Math.Sqrt(3) / 1000;

    private static bool AddMeasurement(List<Iec104Point> target, Dictionary<(string, GridMeasurementKind), int> occurrence,
        GridRtuDefinition rtu, string elementId, string elementName, GridMeasurementKind kind, double range)
    {
        var key = (elementId, kind);
        var index = occurrence.TryGetValue(key, out var seen) ? seen : 0;
        occurrence[key] = index + 1;
        target.Add(new Iec104Point(0, Iec104PointKind.Measurement, rtu.CommonAddress, rtu.Id, elementId, elementName,
            kind, index, range, false));
        return true;
    }

    private static Iec104Point SwitchPoint(GridRtuDefinition rtu, string elementId, string elementName) =>
        new(0, Iec104PointKind.SwitchStatus, rtu.CommonAddress, rtu.Id, elementId, elementName, null, 0, 0, true);

    private static Iec104Point TapPoint(GridRtuDefinition rtu, string elementId, string elementName) =>
        new(0, Iec104PointKind.TapPosition, rtu.CommonAddress, rtu.Id, elementId, elementName, null, 0, 0, true);

    private static Iec104Point ProtectionPoint(GridRtuDefinition rtu, string elementId, string elementName) =>
        new(0, Iec104PointKind.ProtectionTrip, rtu.CommonAddress, rtu.Id, elementId, elementName, null, 0, 0, false);

    private static Iec104Point RtuStatusPoint(GridRtuDefinition rtu) =>
        new(0, Iec104PointKind.RtuStatus, rtu.CommonAddress, rtu.Id, rtu.Id, rtu.Name, null, 0, 0, false);

    private static List<Iec104Point> AssignIoa(IReadOnlyList<Iec104Point> points, int baseIoa)
    {
        var assigned = new List<Iec104Point>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var ioa = baseIoa + i;
            if (ioa > 0xFFFFFF)
                throw new InvalidOperationException("IOA range exhausted.");
            assigned.Add(points[i] with { InformationObjectAddress = ioa });
        }

        return assigned;
    }
}
