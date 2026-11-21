using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.Diagnostics;

/// <summary>
/// Custom metrics and the correlation baggage key. A static <see cref="Meter"/> is the recommended pattern for libraries:
/// instruments cost nothing until a listener (OpenTelemetry in the API host) subscribes to <see cref="MeterName"/>.
/// </summary>
public static class ScadaInstrumentation
{
    public const string MeterName = "Scada";
    public const string CorrelationBaggageKey = "correlation.id";

    private static readonly Meter Meter = new(MeterName, "1.0");

    public static readonly Counter<long> SimulationSteps =
        Meter.CreateCounter<long>("scada.simulation.steps", "{step}", "Simulation steps executed");

    public static readonly Counter<long> Commands =
        Meter.CreateCounter<long>("scada.commands", "{command}", "Operator commands by outcome, reason and source");

    public static readonly Counter<long> Events =
        Meter.CreateCounter<long>("scada.events", "{event}", "Published audit, process and security events by category");

    public static readonly Counter<long> Detections =
        Meter.CreateCounter<long>("scada.detections", "{detection}", "Detections raised by rule and severity");

    public static readonly Counter<long> IncidentsCreated =
        Meter.CreateCounter<long>("scada.incidents.created", "{incident}", "Incidents opened by category and severity");

    public static readonly Counter<long> ScenarioRuns =
        Meter.CreateCounter<long>("scada.scenario.runs", "{run}", "Finished scenario runs by scenario and outcome");

    /// <summary>The correlation id of the current request, if the API host set one on the ambient activity.</summary>
    public static string? CurrentCorrelationId => Activity.Current?.GetBaggageItem(CorrelationBaggageKey);
}
