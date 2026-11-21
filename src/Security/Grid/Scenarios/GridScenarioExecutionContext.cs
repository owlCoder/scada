using Application.Commands;
using Application.Grid;
using Application.Identity;
using Security.Grid.Control;

namespace Security.Grid.Scenarios;

/// <summary>Per-run state handed to every action: the grid services, the run id, the resolved targets and captured sequence numbers.</summary>
public sealed class GridScenarioExecutionContext(
    IGridView view,
    IGridTraining training,
    IGridControl control,
    GridScenarioTargets targets,
    Guid runId,
    CommandReplayGuard replayGuard)
{
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _captured = new(StringComparer.Ordinal);

    public Guid RunId { get; } = runId;
    public IGridView View { get; } = view;
    public IGridTraining Training { get; } = training;
    public IGridControl Control { get; } = control;
    public GridScenarioTargets Targets { get; } = targets;

    /// <summary>
    /// The next sequence number this run would issue for <paramref name="actor"/>: monotonic across runs, because the
    /// platform replay guard is process-wide — a second run must not restart at 1 or every command would be a replay.
    /// </summary>
    public long NextSequence(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var next = Math.Max(_sequences.TryGetValue(actor, out var last) ? last : 0, replayGuard.LastSequence(GridControlService.ReplayKey(actor))) + 1;
        _sequences[actor] = next;
        return next;
    }

    public bool Capture(string key, long sequenceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _captured[key] = sequenceNumber;
        return true;
    }

    public long? Recall(string key) => _captured.TryGetValue(key, out var value) ? value : null;

    /// <summary>The actor used for attacker-style actions: an Administrator system actor (training calls require it).</summary>
    public Actor AttackerActor(string? name = null) => Actor.System(name ?? "grid-attacker");
}
