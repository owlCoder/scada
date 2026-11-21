using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;

namespace Application.Grid;

/// <summary>Owns and serializes all access to the independent grid world.</summary>
public sealed class GridEngine : IGridView
{
    private readonly Lock _sync = new();
    private readonly TimeProvider _time;
    private GridWorld _world;
    private bool _paused;
    private int _version = 1;

    public GridEngine(GridOptions options, TimeProvider time) : this(DefaultGridDefinition.Create(), options, time) { }

    public GridEngine(GridDefinition definition, GridOptions options, TimeProvider time)
    {
        Options = options;
        _time = time;
        var errors = GridDefinitionValidator.Validate(definition).Where(x => !x.StartsWith("Warning:", StringComparison.Ordinal)).Concat(options.Validate()).ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
        _world = new(definition, options.RandomSeed, options.StartTime);
    }

    public GridOptions Options { get; }
    public GridDefinition Definition { get { lock (_sync) return _world.Definition; } }
    public GridStatus Status
    {
        get { lock (_sync) return new(_paused, _world.SimulationTime, _world.StepCount, Options.StepSeconds, Options.TimeScale, _world.Definition.Name, _version); }
    }
    public GridSnapshot Snapshot() { lock (_sync) return _world.Snapshot(_time.GetUtcNow()); }
    public GridSnapshot CaptureState() => Snapshot();
    public T Read<T>(Func<GridWorld, T> query) { lock (_sync) return query(_world); }
    public T Mutate<T>(Func<GridWorld, T> mutation) { lock (_sync) return mutation(_world); }

    public GridStepReport? Step(bool force = false)
    {
        lock (_sync)
        {
            if (_paused && !force) return null;
            _world.Step(Options.StepSeconds);
            return Report();
        }
    }

    public GridStepReport CaptureReport() { lock (_sync) return Report(); }
    public bool SetPaused(bool paused) { lock (_sync) { _paused = paused; return true; } }
    public GridSnapshot Reset()
    {
        lock (_sync) { _world = new(_world.Definition, Options.RandomSeed, Options.StartTime); return _world.Snapshot(_time.GetUtcNow()); }
    }

    public GridSnapshot Load(GridDefinition definition, int version = 1)
    {
        var issues = GridDefinitionValidator.Validate(definition);
        if (GridDefinitionValidator.HasErrors(issues)) throw new ArgumentException(string.Join(" ", issues), nameof(definition));
        lock (_sync)
        {
            var world = new GridWorld(definition, Options.RandomSeed, Options.StartTime);
            if (!world.Snapshot().Converged) throw new ArgumentException("Definition does not converge.", nameof(definition));
            _world = world; _version = version;
            return _world.Snapshot(_time.GetUtcNow());
        }
    }

    private GridStepReport Report() => new(_time.GetUtcNow(), _world.Snapshot(_time.GetUtcNow()), _world.DrainEvents()
        .Select(e => new GridStepEvent(Enum.Parse<GridStepEventKind>(e.Kind), e.SourceId, e.Message, e.Actor, e.Cause)).ToArray());
}
