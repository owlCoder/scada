namespace Domain.Grid.Snapshots;

/// <param name="Converged">True only for a current normal power flow. Fault periods retain prefault power estimates
/// with Uncertain power measurement quality; stale/diverged results mark all measurements Uncertain and raise SolverDivergence.</param>
/// <summary>Complete state of the network after one simulation step (ground truth; redact before showing to operators).</summary>
public sealed record GridSnapshot(
    string Name,
    long Revision,
    TimeSpan SimulationTime,
    long StepCount,
    double FrequencyHz,
    bool Converged,
    int SolverIterations,
    IReadOnlyList<BusSnapshot> Buses,
    IReadOnlyList<BranchSnapshot> Branches,
    IReadOnlyList<SwitchSnapshot> Switches,
    IReadOnlyList<LoadSnapshot> Loads,
    IReadOnlyList<GeneratorSnapshot> Generators,
    IReadOnlyList<CapacitorSnapshot> Capacitors,
    IReadOnlyList<ProtectionSnapshot> Protections,
    IReadOnlyList<GridMeasurementSnapshot> Measurements,
    IReadOnlyList<GridAlarm> Alarms,
    GridTotals Totals)
{
    public BusSnapshot? Bus(string id) => Buses.FirstOrDefault(b => b.Id == id);
    public BranchSnapshot? Branch(string id) => Branches.FirstOrDefault(b => b.Id == id);
    public SwitchSnapshot? Switch(string id) => Switches.FirstOrDefault(s => s.Id == id);
    public LoadSnapshot? Load(string id) => Loads.FirstOrDefault(l => l.Id == id);
}
