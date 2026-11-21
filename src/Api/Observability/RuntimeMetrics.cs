using System.Diagnostics.Metrics;
using Application.Persistence;
using Application.Simulation;

namespace Api.Observability;

/// <summary>Observable gauges for host state that is read on demand (meter <see cref="MeterName"/>).</summary>
public sealed class RuntimeMetrics
{
    public const string MeterName = "Scada.Runtime";

    public RuntimeMetrics(IMeterFactory meters, IPersistenceQueue queue, SimulationEngine engine)
    {
        var meter = meters.Create(MeterName);
        meter.CreateObservableGauge("scada.persistence.queue.pending", () => queue.Status.Pending, "{item}", "Items waiting to be persisted");
        meter.CreateObservableCounter("scada.persistence.dropped", () => queue.Status.Dropped, "{item}", "Items dropped because the queue was full");
        meter.CreateObservableCounter("scada.persistence.failed", () => queue.Status.Failed, "{item}", "Items that failed after all retries");
        meter.CreateObservableGauge("scada.simulation.time", () => engine.Status.SimulationTime.TotalSeconds, "s", "Simulated time");
        meter.CreateObservableGauge("scada.simulation.paused", () => engine.Status.IsPaused ? 1 : 0, "{state}", "1 while the simulation is paused");
    }
}
