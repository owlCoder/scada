using Application.Grid;
using Application.Identity;

namespace Security.Grid.Scenarios;

/// <summary>Per-run state handed to every action: the grid services, the run id, the resolved targets and captured sequence numbers.</summary>
public sealed class GridScenarioExecutionContext(
    IGridView view,
    IGridTraining training,
    IGridControl control,
    GridScenarioTargets targets,
    Guid runId)
{
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _captured = new(StringComparer.Ordinal);

    public Guid RunId { get; } = runId;
    public IGridView View { get; } = view;
    public IGridTraining Training { get; } = training;
    public IGridControl Control { get; } = control;
    public GridScenarioTargets Targets { get; } = targets;

    /// <summary>The next sequence number this run would issue for <paramref name="actor"/> (tracks what the run already sent).</summary>
    public long NextSequence(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var next = _sequences.TryGetValue(actor, out var last) ? last + 1 : 1;
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
