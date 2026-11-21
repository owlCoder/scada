using Domain.Process.Alarms;

namespace Domain.Process.Snapshots;

public sealed record PlantSnapshot(
    string PlantName,
    long Revision,
    TimeSpan SimulationTime,
    long StepCount,
    bool InterlocksBypassed,
    IReadOnlyList<TankSnapshot> Tanks,
    IReadOnlyList<PumpSnapshot> Pumps,
    IReadOnlyList<ValveSnapshot> Valves,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<ProcessAlarm> Alarms)
{
    public TankSnapshot? Tank(string id) => Tanks.FirstOrDefault(t => t.Id == id);
    public PumpSnapshot? Pump(string id) => Pumps.FirstOrDefault(p => p.Id == id);
    public ValveSnapshot? Valve(string id) => Valves.FirstOrDefault(v => v.Id == id);
    public SensorSnapshot? Sensor(string id) => Sensors.FirstOrDefault(s => s.Id == id);
}
