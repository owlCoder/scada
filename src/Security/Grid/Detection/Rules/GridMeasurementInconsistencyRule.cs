using System.Globalization;
using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;
using Domain.Process.Devices;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Kirchhoff / state-estimation residual: at every bus the reported injections (generation minus load) must equal the
/// reported branch flows leaving the bus, within <c>Threshold</c> percent. A single reported measurement that deviates
/// from its true value by more than the threshold is also reported. Normal telemetry noise (0.2 %) stays far below the
/// default 2 % threshold, so this rule is silent in normal operation and fires on false data injection.
/// </summary>
public sealed class GridMeasurementInconsistencyRule(IGridView view) : GridDetectionRuleBase
{
    private readonly IGridView _view = view ?? throw new ArgumentNullException(nameof(view));

    public override string Id => "grid-measurement-inconsistency";
    public override string Name => "Measurement inconsistency";
    public override string Description => "Reported measurements do not balance (Kirchhoff residual) or deviate from physical values — likely false data injection.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 2, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        var tolerance = Math.Max(0.5, settings.Threshold);

        // (a) A telemetered value that deviates from its true value beyond the tolerance (spoofed measurement).
        foreach (var measurement in context.Current.Measurements.Where(m => m.Quality == MeasurementQuality.Good))
        {
            var deviation = Math.Abs(measurement.ReportedValue - measurement.TrueValue)
                / Math.Max(0.001, Math.Abs(measurement.TrueValue)) * 100.0;
            if (deviation <= tolerance)
                continue;
            findings.Add(new($"measurement:{measurement.Id}", $"Measurement {measurement.Id} deviates from physical value",
                $"{measurement.Kind} on {measurement.ElementId} reports {measurement.ReportedValue:0.###} {measurement.Unit} " +
                $"but the network state implies {measurement.TrueValue:0.###} ({deviation:0.#}% deviation).",
                measurement.ElementId,
                [Evidence("measurement", measurement.Id,
                    string.Create(CultureInfo.InvariantCulture, $"reported {measurement.ReportedValue:0.####}, true {measurement.TrueValue:0.####}, deviation {deviation:0.##}%"))]));
        }

        // (b) Kirchhoff residual per bus using reported values where telemetered, true values otherwise.
        // Buses fed by an upstream source are excluded: the source injection is the unmeasured slack.
        var sourceBuses = _view.Definition.Sources.Select(s => s.BusId).ToHashSet(StringComparer.Ordinal);
        foreach (var bus in context.Current.Buses.Where(b => !sourceBuses.Contains(b.Id)))
        {
            var generation = 0.0;
            foreach (var generator in context.Current.Generators.Where(g => g.BusId == bus.Id && g.Connected))
                generation += ReportedPower(context, generator.Id, generator.ActivePowerMw);

            var load = 0.0;
            foreach (var busLoad in context.Current.Loads.Where(l => l.BusId == bus.Id && l.Energized && !l.Shed))
                load += ReportedPower(context, busLoad.Id, busLoad.ActivePowerMw);

            var outflow = 0.0;
            foreach (var branch in context.Current.Branches)
            {
                if (branch.FromBusId == bus.Id)
                    outflow += branch.Energized ? ReportedBranchPower(context, branch, true) : 0;
                else if (branch.ToBusId == bus.Id)
                    outflow -= branch.Energized ? ReportedBranchPower(context, branch, false) : 0;
            }

            var scale = Math.Max(0.1, generation + load + Math.Abs(outflow));
            var residual = Math.Abs(generation - load - outflow) / scale * 100.0;
            if (residual <= tolerance)
                continue;
            findings.Add(new($"bus:{bus.Id}", $"Power balance residual at {bus.Name}",
                $"At {bus.Id} reported generation {generation:0.###} MW minus load {load:0.###} MW does not match reported " +
                $"branch flows {outflow:0.###} MW (residual {residual:0.#}%, tolerance {tolerance:0.#}%).",
                bus.Id,
                [Evidence("bus", bus.Id, string.Create(CultureInfo.InvariantCulture,
                    $"generation {generation:0.###} MW, load {load:0.###} MW, outflow {outflow:0.###} MW, residual {residual:0.##}%"))]));
        }

        return findings;
    }

    private static double ReportedPower(GridDetectionContext context, string elementId, double trueValue) =>
        context.Current.Measurements.FirstOrDefault(m => m.ElementId == elementId && m.Kind == GridMeasurementKind.ActivePower)
            is { } measurement && measurement.Quality == MeasurementQuality.Good
            ? measurement.ReportedValue
            : trueValue;

    private static double ReportedBranchPower(GridDetectionContext context, BranchSnapshot branch, bool fromEnd)
    {
        var trueValue = fromEnd ? branch.ActivePowerFromMw : branch.ActivePowerToMw;
        return context.Current.Measurements.FirstOrDefault(m => m.ElementId == branch.Id && m.Kind == GridMeasurementKind.ActivePower)
            is { } measurement && measurement.Quality == MeasurementQuality.Good
            ? measurement.ReportedValue
            : trueValue;
    }
}
