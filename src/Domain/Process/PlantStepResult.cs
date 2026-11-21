using Domain.Process.Alarms;
using Domain.Process.Commands;
using Domain.Process.Safety;
using Domain.Process.Snapshots;

namespace Domain.Process;

public sealed record PlantStepResult(
    PlantSnapshot Snapshot,
    IReadOnlyList<DeviceChange> DeviceChanges,
    IReadOnlyList<InterlockActivation> Interlocks,
    IReadOnlyList<SafetyViolation> SafetyViolations,
    IReadOnlyList<AlarmTransition> AlarmTransitions);
