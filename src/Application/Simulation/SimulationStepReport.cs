using Domain.Control;
using Domain.Network;
using Domain.Process;

namespace Application.Simulation;

public sealed record SimulationStepReport(
    DateTimeOffset Timestamp,
    PlantStepResult Plant,
    IReadOnlyList<ControllerScanResult> Scans,
    IReadOnlyList<ControllerCommandRecord> ControllerCommands,
    IReadOnlyList<SupervisoryStatusChange> SupervisoryChanges,
    IReadOnlyList<ControllerSnapshot> Controllers,
    NetworkSnapshot Network);
