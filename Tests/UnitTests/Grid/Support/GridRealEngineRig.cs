using Application.Commands;
using Application.Detection;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Incidents;
using Application.Realtime;
using Application.Scenarios;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Commands;
using Security.Grid.Control;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using Security.Grid.Scenarios;
using Security.Grid.Scenarios.Catalog;

namespace UnitTests.Grid.Support;

/// <summary>
/// Wires the REAL grid stack — <see cref="GridEngine"/>, <see cref="GridCoordinator"/>, <see cref="GridControlService"/>,
/// <see cref="GridTrainingService"/>, <see cref="GridDetectionEngine"/> and <see cref="GridScenarioEngine"/> — over
/// in-memory stores with a fake clock, so tests can drive <see cref="GridCoordinator.StepAsync"/> deterministically
/// (no wall-clock waits, no hosted loop). Mirrors the production registration order.
/// </summary>
public sealed class GridRealEngineRig
{
    public GridRealEngineRig(double stepSeconds = 1, int seed = 104)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero));
        Options = new GridOptions { StepSeconds = stepSeconds, RandomSeed = seed };
        Engine = new GridEngine(Options, Time);
        Store = new InMemoryGridDefinitionStore(Time);
        Buffer = new RecentEventBuffer();
        Queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero }, Time,
            NullLogger<ChannelPersistenceQueue>.Instance);
        Realtime = new RealtimeBroadcaster(Time);
        Events = new EventPublisher(Buffer, Queue, Realtime);
        Incidents = new IncidentService(IncidentStore, Events, Realtime, Time);
        DetectionStore = new InMemoryDetectionStore();
        RunStore = new InMemoryScenarioRunStore();
        // The coordinator materialises the observer list on the first step, so the engines can be wired after it.
        var observers = new List<IGridObserver>();
        Coordinator = new GridCoordinator(Engine, Store, Events, Realtime, observers, Time, NullLogger<GridCoordinator>.Instance);
        Training = new GridTrainingService(Coordinator);
        ReplayGuard = new CommandReplayGuard();
        Control = new GridControlService(Coordinator, Options, Time,
            new CommandMonitor(Events, Time, new CommandRateOptions()), ReplayGuard);
        Scenarios = new GridScenarioEngine(DefaultGridScenarios.Create(), Engine, Training, Control, DetectionStore, RunStore,
            Events, Realtime, Time, ReplayGuard, NullLogger<GridScenarioEngine>.Instance);
        Detection = new GridDetectionEngine(DefaultGridDetectionRules.Create(Engine), Buffer, DetectionStore, Incidents,
            Events, Realtime, Scenarios, Engine, NullLogger<GridDetectionEngine>.Instance);
        observers.Add(Detection);
        observers.Add(Scenarios);
    }

    public FakeTimeProvider Time { get; }
    public GridOptions Options { get; }
    public GridEngine Engine { get; }
    public InMemoryGridDefinitionStore Store { get; }
    public RecentEventBuffer Buffer { get; }
    public ChannelPersistenceQueue Queue { get; }
    public RealtimeBroadcaster Realtime { get; }
    public EventPublisher Events { get; }
    public IncidentService Incidents { get; }
    public InMemoryIncidentStore IncidentStore { get; } = new();
    public InMemoryDetectionStore DetectionStore { get; }
    public InMemoryScenarioRunStore RunStore { get; }
    public GridCoordinator Coordinator { get; }
    public GridTrainingService Training { get; }
    public GridControlService Control { get; }
    public CommandReplayGuard ReplayGuard { get; }
    public GridScenarioEngine Scenarios { get; }
    public GridDetectionEngine Detection { get; }
    public Actor Instructor { get; } = Actor.System("instructor");

    /// <summary>Advances fake time by one step and runs the coordinator step, notifying all grid observers.</summary>
    public async Task<GridStepReport?> StepAsync()
    {
        Time.Advance(TimeSpan.FromSeconds(Options.StepSeconds));
        return await Coordinator.StepAsync(false, CancellationToken.None);
    }

    public async Task<int> RunForAsync(double simulatedSeconds)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(simulatedSeconds / Options.StepSeconds));
        for (var i = 0; i < steps; i++)
            await StepAsync();
        return steps;
    }

    /// <summary>Starts a catalog scenario and steps until the run ends; returns the finished run record.</summary>
    public async Task<ScenarioRunRecord> RunScenarioAsync(string scenarioId, double maxSeconds = 600)
    {
        var start = await Scenarios.StartAsync(scenarioId, Instructor, CancellationToken.None);
        if (!start.Succeeded)
            throw new InvalidOperationException(start.Message);
        var steps = (int)(maxSeconds / Options.StepSeconds) + 10;
        for (var i = 0; i < steps && Scenarios.ActiveRunId is not null; i++)
            await StepAsync();
        return Scenarios.Current ?? throw new InvalidOperationException("No scenario run was recorded.");
    }

    public async Task<IReadOnlyList<DetectionRecord>> DetectionsAsync() =>
        await DetectionStore.QueryAsync(new DetectionQuery(Limit: 10_000), CancellationToken.None);
}
