using Application.Diagnostics;
using System.Globalization;
using Application.Detection;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Realtime;
using Application.Scenarios;
using Microsoft.Extensions.Logging;
using Security.Detection;
using Security.Grid.Detection;
using Security.Scenarios;

namespace Security.Grid.Scenarios;

/// <summary>
/// Runs one grid training scenario at a time against the grid simulation, exactly like the water
/// <see cref="ScenarioEngine"/> but over <see cref="IGridObserver"/> steps. Targets are resolved generically at run start;
/// actions only act through <see cref="IGridTraining"/> and <see cref="IGridControl"/>. Elapsed time is simulated time and
/// advances only while the run is <see cref="ScenarioRunState.Running"/>. When a run ends its detections are collected by
/// run id, the outcome is scored, cleanup actions run, training defaults are restored and the run is persisted.
/// </summary>
public sealed class GridScenarioEngine : IGridObserver, IGridActiveScenarioProvider
{
    private const int PublishEverySteps = 10;
    private const int MaxDetectionsQueried = 1000;
    private static readonly Actor SystemActor = Actor.System("grid-scenario-engine");

    private readonly Dictionary<string, GridScenarioDefinition> _definitions;
    private readonly IGridView _view;
    private readonly IGridTraining _training;
    private readonly IGridControl _control;
    private readonly IDetectionStore _detections;
    private readonly IScenarioRunStore _runs;
    private readonly IEventPublisher _events;
    private readonly IRealtimePublisher _realtime;
    private readonly TimeProvider _time;
    private readonly ILogger<GridScenarioEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _snapshotSync = new();
    private GridScenarioRun? _active;
    private Guid? _activeRunId;
    private ScenarioRunRecord? _current;

    public GridScenarioEngine(
        IEnumerable<GridScenarioDefinition> definitions,
        IGridView view,
        IGridTraining training,
        IGridControl control,
        IDetectionStore detections,
        IScenarioRunStore runs,
        IEventPublisher events,
        IRealtimePublisher realtime,
        TimeProvider time,
        ILogger<GridScenarioEngine> logger)
    {
        var list = definitions.ToArray();
        var errors = list.SelectMany(d => d.Validate()).ToList();
        errors.AddRange(list.GroupBy(d => d.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"Grid scenario id {g.Key} is defined more than once."));
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(definitions));

        _definitions = list.ToDictionary(d => d.Id, StringComparer.Ordinal);
        _view = view;
        _training = training;
        _control = control;
        _detections = detections;
        _runs = runs;
        _events = events;
        _realtime = realtime;
        _time = time;
        _logger = logger;
    }

    public IReadOnlyList<ScenarioDescriptor> Scenarios => _definitions.Values
        .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        .Select(d => new ScenarioDescriptor(d.Id, d.Name, d.Category, d.Description, d.DurationSeconds, d.ExpectedDetections,
            d.Actions.Concat(d.Cleanup ?? []).OrderBy(a => a.AtSeconds)
                .Select(a => string.Create(CultureInfo.InvariantCulture, $"t={a.AtSeconds:0.#}s: {a.Spec.Describe()}")).ToArray()))
        .ToArray();

    public Guid? ActiveRunId
    {
        get
        {
            lock (_snapshotSync)
                return _activeRunId;
        }
    }

    /// <summary>The active run, or the most recently finished one.</summary>
    public ScenarioRunRecord? Current
    {
        get
        {
            lock (_snapshotSync)
                return _current;
        }
    }

    public Task<ScenarioControlResult> StartAsync(string scenarioId, Actor actor, CancellationToken cancellationToken) =>
        _definitions.TryGetValue(scenarioId ?? "", out var definition)
            ? StartCoreAsync(definition, actor, null, cancellationToken)
            : Task.FromResult(ScenarioControlResult.Fail($"Unknown grid scenario '{scenarioId}'."));

    public async Task<ScenarioControlResult> ReplayAsync(Guid runId, Actor actor, CancellationToken cancellationToken)
    {
        var previous = await _runs.GetAsync(runId, cancellationToken);
        if (previous is null)
            return ScenarioControlResult.Fail($"Grid scenario run {runId} was not found.");
        return _definitions.TryGetValue(previous.ScenarioId, out var definition)
            ? await StartCoreAsync(definition, actor, runId, cancellationToken)
            : ScenarioControlResult.Fail($"Grid scenario '{previous.ScenarioId}' is no longer defined.");
    }

    public Task<ScenarioControlResult> PauseAsync(Actor actor, CancellationToken cancellationToken) =>
        TransitionAsync(actor, "pause", ScenarioRunState.Running, ScenarioRunState.Paused, cancellationToken);

    public Task<ScenarioControlResult> ResumeAsync(Actor actor, CancellationToken cancellationToken) =>
        TransitionAsync(actor, "resume", ScenarioRunState.Paused, ScenarioRunState.Running, cancellationToken);

    public async Task<ScenarioControlResult> StopAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorize(actor, "stop"))
            return ScenarioControlResult.Fail("Administrator role required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not { } run)
                return ScenarioControlResult.Fail("No grid scenario is active.", Current);

            var record = await FinishAsync(run, ScenarioRunState.Stopped, actor, cancellationToken);
            return ScenarioControlResult.Ok($"Grid scenario {run.Definition.Name} stopped.", record);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Aborts any active run, restores training defaults and clears the current run view.</summary>
    public async Task<ScenarioControlResult> ResetAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorize(actor, "reset"))
            return ScenarioControlResult.Fail("Administrator role required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var aborted = _active is { } run ? await FinishAsync(run, ScenarioRunState.Stopped, actor, cancellationToken) : null;
            var restored = aborted is null ? await RestoreDefaultsAsync(cancellationToken) : 0;
            lock (_snapshotSync)
                _current = null;
            _realtime.Publish(RealtimeTopics.Scenarios, new { Reset = true });
            Publish(EventSeverity.Information, "Grid scenario environment reset", actor.Name, null);
            return ScenarioControlResult.Ok(aborted is null
                ? $"Grid scenario environment reset; {restored} training change(s) restored."
                : $"Grid scenario {aborted.ScenarioName} aborted and environment reset.", null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ScenarioRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var current = Current;
        return current?.RunId == runId ? Task.FromResult<ScenarioRunRecord?>(current) : _runs.GetAsync(runId, cancellationToken);
    }

    public Task<IReadOnlyList<ScenarioRunRecord>> ListRunsAsync(int limit, CancellationToken cancellationToken) =>
        _runs.ListAsync(Math.Clamp(limit, 1, 500), cancellationToken);

    public async ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not { State: ScenarioRunState.Running } run)
                return 0;

            run.Measurements.Observe(report);
            run.ElapsedSeconds += StepSeconds();
            var executed = 0;
            while (run.TakeDue() is { } scheduled)
            {
                await ExecuteAsync(run, scheduled.Spec, TimeSpan.FromSeconds(scheduled.AtSeconds), cancellationToken);
                executed++;
            }

            if (run.IsComplete)
            {
                await FinishAsync(run, ScenarioRunState.Completed, SystemActor, cancellationToken);
                return executed + 1;
            }

            var publish = executed > 0 || ++run.StepsSincePublish >= PublishEverySteps;
            var record = run.ToRecord(null, [], ScenarioOutcome.Pending);
            if (publish)
            {
                if (await PersistCurrentAsync(record, run.RunId, cancellationToken))
                    run.StepsSincePublish = 0;
            }
            else
                UpdateCurrent(record, run.RunId, publish: false);

            return executed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private double StepSeconds()
    {
        var step = _view.Status.StepSeconds;
        return step > 0 ? step : 1;
    }

    private async Task<ScenarioControlResult> StartCoreAsync(GridScenarioDefinition definition, Actor actor, Guid? replayOf,
        CancellationToken cancellationToken)
    {
        if (!Authorize(actor, $"start grid scenario {definition.Id}"))
            return ScenarioControlResult.Fail("Administrator role required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is { } existing)
                return ScenarioControlResult.Fail($"Grid scenario {existing.Definition.Name} is already active.", Current);

            var resolution = GridScenarioTargetResolver.Resolve(_view, definition.RequiredTargets().ToArray());
            if (!resolution.Succeeded)
                return ScenarioControlResult.Fail(
                    $"Grid scenario {definition.Name} cannot run on this network: {string.Join(" ", resolution.Errors)}", Current);

            var runId = Guid.NewGuid();
            var snapshot = _view.Snapshot();
            var run = new GridScenarioRun(definition,
                new GridScenarioExecutionContext(_view, _training, _control, resolution.Targets!, runId),
                new GridScenarioMeasurements(snapshot), actor.Name, _time.GetUtcNow(), snapshot.SimulationTime, replayOf);
            var record = run.ToRecord(null, [], ScenarioOutcome.Pending);
            if (!await PersistCurrentAsync(record, runId, cancellationToken))
                return ScenarioControlResult.Fail("Could not save the grid scenario run. Please try again.", Current);
            _active = run;
            Publish(EventSeverity.Medium, replayOf is null
                ? $"Grid scenario {definition.Name} started"
                : $"Grid scenario {definition.Name} started as a replay of run {replayOf}", actor.Name, runId);
            return ScenarioControlResult.Ok($"Grid scenario {definition.Name} started.", record);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ScenarioControlResult> TransitionAsync(Actor actor, string verb, ScenarioRunState from, ScenarioRunState to,
        CancellationToken cancellationToken)
    {
        if (!Authorize(actor, verb))
            return ScenarioControlResult.Fail("Administrator role required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not { } run)
                return ScenarioControlResult.Fail("No grid scenario is active.", Current);
            if (run.State != from)
                return ScenarioControlResult.Fail($"Cannot {verb} a grid scenario that is {run.State}.", Current);

            run.State = to;
            var record = run.ToRecord(null, [], ScenarioOutcome.Pending);
            var saved = false;
            try
            {
                saved = await PersistCurrentAsync(record, run.RunId, cancellationToken);
            }
            finally
            {
                if (!saved)
                    run.State = from;
            }
            if (!saved)
                return ScenarioControlResult.Fail("Could not save the grid scenario change. Please try again.", Current);
            Publish(EventSeverity.Information, $"Grid scenario {run.Definition.Name} {to.ToString().ToLowerInvariant()}", actor.Name, run.RunId);
            return ScenarioControlResult.Ok($"Grid scenario {run.Definition.Name} {to.ToString().ToLowerInvariant()}.", record);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ScenarioRunRecord> FinishAsync(GridScenarioRun run, ScenarioRunState state, Actor actor,
        CancellationToken cancellationToken)
    {
        run.State = state;
        var offset = TimeSpan.FromSeconds(run.ElapsedSeconds);
        foreach (var cleanup in run.Definition.Cleanup ?? [])
            await ExecuteAsync(run, cleanup.Spec, offset, cancellationToken);

        IReadOnlyList<string> observed;
        try
        {
            observed = (await _detections.QueryAsync(new DetectionQuery(ScenarioRunId: run.RunId, Limit: MaxDetectionsQueried), cancellationToken))
                .Select(d => d.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not query detections for grid scenario run {RunId}", run.RunId);
            observed = [];
        }

        var outcome = ScenarioOutcomeEvaluator.Evaluate(state, run.Definition.ExpectedDetections, observed);
        var restored = await RestoreDefaultsAsync(cancellationToken);
        _active = null;
        var record = run.ToRecord(_time.GetUtcNow(), observed, outcome);
        ScadaInstrumentation.ScenarioRuns.Add(1, new KeyValuePair<string, object?>("scenario", run.Definition.Id),
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        // Completion restores the environment even if persistence is unavailable, but never advertises an unsaved result.
        lock (_snapshotSync)
            _activeRunId = null;
        await PersistCurrentAsync(record, null, cancellationToken);

        var missing = run.Definition.ExpectedDetections.Except(observed, StringComparer.Ordinal).ToArray();
        Publish(outcome is ScenarioOutcome.Detected or ScenarioOutcome.Aborted ? EventSeverity.Information : EventSeverity.Medium,
            $"Grid scenario {run.Definition.Name} {state.ToString().ToLowerInvariant()}: {outcome}" +
            (missing.Length > 0 && state == ScenarioRunState.Completed ? $" (missed: {string.Join(", ", missing)})" : "") +
            $"; {restored} training change(s) restored", actor.Name, run.RunId);
        return record;
    }

    private async Task<int> RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _training.RestoreTrainingDefaultsAsync(SystemActor, cancellationToken);
            return result.Accepted ? 1 : 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not restore grid training defaults");
            return 0;
        }
    }

    private async Task<bool> ExecuteAsync(GridScenarioRun run, GridScenarioActionSpec spec, TimeSpan offset, CancellationToken cancellationToken)
    {
        ScenarioActionResult result;
        try
        {
            result = await GridScenarioActionExecutor.ExecuteAsync(spec, run.Context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Grid scenario action '{Action}' failed in run {RunId}", spec.Describe(), run.RunId);
            result = ScenarioActionResult.Failed($"Unexpected error: {exception.GetType().Name}");
        }

        run.Log(new ScenarioActionLog(offset, _time.GetUtcNow(), spec.Describe(), result.Succeeded, result.Detail));
        return result.Succeeded;
    }

    private bool Authorize(Actor actor, string operation)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.HasRole(AccessRole.Administrator))
            return true;

        _events.Publish(SecurityEvent.Create(_time.GetUtcNow(), EventCategories.AuthorizationDenied, EventSeverity.Medium,
            "grid-scenario-engine", $"Denied attempt to {operation}: requires Administrator, caller is {actor.Role}", actor.Name));
        return false;
    }

    private bool UpdateCurrent(ScenarioRunRecord record, Guid? activeRunId, bool publish = true)
    {
        lock (_snapshotSync)
        {
            _current = record;
            _activeRunId = activeRunId;
        }

        return publish && _realtime.Publish(RealtimeTopics.Scenarios, record);
    }

    private async Task<bool> PersistCurrentAsync(ScenarioRunRecord record, Guid? activeRunId, CancellationToken cancellationToken)
    {
        if (!await SaveAsync(record, cancellationToken))
            return false;
        UpdateCurrent(record, activeRunId);
        return true;
    }

    private async Task<bool> SaveAsync(ScenarioRunRecord record, CancellationToken cancellationToken)
    {
        try
        {
            return await _runs.SaveAsync(record, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not persist grid scenario run {RunId}", record.RunId);
            return false;
        }
    }

    private bool Publish(EventSeverity severity, string message, string actor, Guid? runId) =>
        _events.Publish(SecurityEvent.Create(_time.GetUtcNow(), EventCategories.Scenario, severity, "grid-scenario-engine", message,
            actor, null, runId));
}
