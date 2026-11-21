using System.Globalization;
using Application.Detection;
using Domain.Incidents;
using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Security.Detection.Rules;

/// <summary>
/// Flags measurements that change faster than the process physically allows while the associated
/// equipment is in a steady state. <c>Threshold</c> is the maximum level rate in percent per second;
/// flow and pressure limits scale from it.
/// </summary>
public sealed class ImpossibleMeasurementChangeRule : DetectionRuleBase
{
    public override string Id => "impossible-measurement-change";
    public override string Name => "Physically impossible measurement change";
    public override string Description => "Measurement rate of change beyond physical limits while equipment is steady.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 2.0, 60, 120);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        if (context.Previous is null)
            return [];

        var findings = new List<DetectionFinding>();
        foreach (var current in context.Current.Sensors)
        {
            var previous = context.Previous.Sensor(current.Id);
            if (previous is null || current.Quality == MeasurementQuality.Bad || previous.Quality == MeasurementQuality.Bad)
                continue;

            var limit = current.Kind switch
            {
                SensorKind.Level => settings.Threshold,
                SensorKind.Flow => settings.Threshold,
                _ => settings.Threshold * 1.5
            };
            if (current.Kind != SensorKind.Level && !IsSteady(context, current.DeviceId))
                continue;

            var rate = Math.Abs(current.ReportedValue - previous.ReportedValue) / context.StepSeconds;
            if (rate <= limit)
                continue;

            var c = CultureInfo.InvariantCulture;
            findings.Add(new(current.Id, $"Impossible change on {current.Id}",
                string.Create(c, $"{current.Id} moved from {previous.ReportedValue:0.###} to {current.ReportedValue:0.###} {current.Unit} in {context.StepSeconds}s ({rate:0.##}/s, limit {limit:0.##}/s)."),
                current.DeviceId,
                [Evidence("measurement", current.Id, string.Create(c, $"{previous.ReportedValue:0.###} -> {current.ReportedValue:0.###} {current.Unit}"))]));
        }

        return findings;
    }

    private static bool IsSteady(DetectionContext context, string deviceId)
    {
        var previous = context.Previous!;
        if (context.Current.Pump(deviceId) is { } pump && previous.Pump(deviceId) is { } before)
            return pump.State == before.State && pump.Health != DeviceHealth.Faulted && before.Health != DeviceHealth.Faulted &&
                Math.Abs(pump.Speed - before.Speed) < 0.001 && DischargeSteady(context.Current, previous, pump);
        if (context.Current.Valve(deviceId) is { } valve && previous.Valve(deviceId) is { } prior)
            return Math.Abs(valve.Position - prior.Position) < 0.01 && valve.Health != DeviceHealth.Faulted;
        return true;
    }

    private static bool DischargeSteady(PlantSnapshot current, PlantSnapshot previous, PumpSnapshot pump) =>
        current.Valve(pump.DischargeValveId) is { } valve && previous.Valve(pump.DischargeValveId) is { } before &&
        Math.Abs(valve.Position - before.Position) < 0.01;
}
