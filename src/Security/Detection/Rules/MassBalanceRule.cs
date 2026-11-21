using System.Globalization;
using Application.Detection;
using Domain.Incidents;
using Domain.Process.Configuration;
using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Security.Detection.Rules;

/// <summary>
/// Compares the level change reported by each tank's level transmitter with the change implied by the
/// flow transmitters around the tank. A persistent difference indicates spoofed, injected or faulty
/// measurements. <c>Threshold</c> is the tolerated difference in percent; <c>WindowSeconds</c> is the
/// evaluation window in simulated seconds.
/// </summary>
public sealed class MassBalanceRule : DetectionRuleBase
{
    private readonly IReadOnlyList<TankBalance> _balances;
    private readonly Dictionary<string, Queue<(double Expected, double Actual)>> _windows = new(StringComparer.Ordinal);

    public MassBalanceRule(PlantDefinition plant)
    {
        ArgumentNullException.ThrowIfNull(plant);
        var sensorsByDevice = plant.Sensors.Where(s => s.Kind == SensorKind.Flow)
            .GroupBy(s => s.DeviceId).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var balances = new List<TankBalance>();
        foreach (var tank in plant.Tanks)
        {
            var level = plant.Sensors.FirstOrDefault(s => s.Kind == SensorKind.Level && s.DeviceId == tank.Id);
            var inflows = plant.Pumps.Where(p => p.TargetTankId == tank.Id).Select(p => p.Id)
                .Concat(plant.Valves.Where(v => v.Role == ValveRole.Inlet && v.TankId == tank.Id).Select(v => v.Id)).ToArray();
            var outflows = plant.Pumps.Where(p => p.SourceTankId == tank.Id).Select(p => p.Id)
                .Concat(plant.Valves.Where(v => v.Role is ValveRole.Outlet or ValveRole.Drain && v.TankId == tank.Id).Select(v => v.Id)).ToArray();
            if (level is null || inflows.Concat(outflows).Any(d => !sensorsByDevice.ContainsKey(d)))
                continue;

            balances.Add(new TankBalance(tank.Id, tank.CapacityCubicMeters, level.Id,
                inflows.Select(d => sensorsByDevice[d]).ToArray(), outflows.Select(d => sensorsByDevice[d]).ToArray()));
        }

        _balances = balances;
    }

    public override int ResetState()
    {
        var cleared = _windows.Count;
        _windows.Clear();
        return cleared;
    }

    public override string Id => "mass-balance-inconsistency";
    public override string Name => "Sensor and process inconsistency";
    public override string Description => "Tank level changes that do not match measured inflows and outflows.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 3.0, 60, 300);

    public IReadOnlyList<string> EvaluatedTanks => _balances.Select(b => b.TankId).ToArray();

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        if (context.Previous is null)
            return [];

        var findings = new List<DetectionFinding>();
        var capacity = Math.Max(2, (int)Math.Round(settings.WindowSeconds / context.StepSeconds));
        foreach (var balance in _balances)
        {
            var window = _windows.TryGetValue(balance.TankId, out var existing) ? existing : _windows[balance.TankId] = new();
            var current = context.Current.Sensor(balance.LevelSensorId);
            var previous = context.Previous.Sensor(balance.LevelSensorId);
            if (current is null || previous is null || !Usable(current) || !Usable(previous))
            {
                window.Clear();
                continue;
            }

            var netFlow = balance.InflowSensors.Sum(id => Flow(context.Current, id)) - balance.OutflowSensors.Sum(id => Flow(context.Current, id));
            var expected = netFlow * context.StepSeconds / 60 / balance.Capacity * 100;
            window.Enqueue((expected, current.ReportedValue - previous.ReportedValue));
            while (window.Count > capacity)
                window.Dequeue();
            if (window.Count < capacity)
                continue;

            var expectedTotal = window.Sum(x => x.Expected);
            var actualTotal = window.Sum(x => x.Actual);
            var difference = Math.Abs(expectedTotal - actualTotal);
            if (difference <= settings.Threshold)
                continue;

            var c = CultureInfo.InvariantCulture;
            findings.Add(new(balance.TankId, $"Level and flow measurements disagree on {balance.TankId}",
                string.Create(c, $"Over {settings.WindowSeconds}s the flows imply {expectedTotal:+0.00;-0.00}% but {balance.LevelSensorId} changed {actualTotal:+0.00;-0.00}%."),
                balance.TankId,
                [
                    Evidence("measurement", balance.LevelSensorId, string.Create(c, $"Reported change {actualTotal:0.00}%")),
                    Evidence("calculation", string.Join(",", balance.InflowSensors.Concat(balance.OutflowSensors)),
                        string.Create(c, $"Expected change {expectedTotal:0.00}%, difference {difference:0.00}%"))
                ]));
            window.Clear();
        }

        return findings;
    }

    private static bool Usable(SensorSnapshot sensor) =>
        sensor.Quality == MeasurementQuality.Good && sensor.ReportedValue is > 0.5 and < 99.5;

    private static double Flow(PlantSnapshot snapshot, string sensorId) => snapshot.Sensor(sensorId)?.ReportedValue ?? 0;
}
