using Application.Commands;
using Application.Detection;
using Application.Events;
using global::Application.Grid;
using Application.Identity;
using Application.Incidents;
using Application.Realtime;
using global::Application.Scenarios;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using Security.Grid.Scenarios;
using Security.Grid.Scenarios.Catalog;
using UnitTests.Security.Grid.Support;

namespace UnitTests.Security.Grid.Support;

/// <summary>
/// Wires the grid security stack (fake view/training/control + detection engine + scenario engine) over in-memory stores
/// with a fake clock, mirroring <see cref="TestRig"/> for grid mode.
/// </summary>
public sealed class SecurityGridTestRig
{
    public SecurityGridTestRig(global::Domain.Grid.Definition.GridDefinition? network = null, int seed = 42)
    {
        Time = new FakeTimeProvider(SecurityFakeGridView.Start);
        View = new SecurityFakeGridView(network ?? SecurityFakeGridNetworks.Default(), stepSeconds: 1, seed);
        Training = new SecurityFakeGridTraining(View);
        Control = new SecurityFakeGridControl(View);
        Buffer = new RecentEventBuffer();
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero }, Time,
            NullLogger<ChannelPersistenceQueue>.Instance);
        Realtime = new RealtimeBroadcaster(Time);
        Events = new EventPublisher(Buffer, queue, Realtime);
        Incidents = new IncidentService(IncidentStore, Events, Realtime, Time);
        DetectionStore = new InMemoryDetectionStore();
        RunStore = new InMemoryScenarioRunStore();
        Scenarios = new GridScenarioEngine(DefaultGridScenarios.Create(), View, Training, Control, DetectionStore, RunStore,
            Events, Realtime, Time, new CommandReplayGuard(), NullLogger<GridScenarioEngine>.Instance);
        Detection = new GridDetectionEngine(DefaultGridDetectionRules.Create(View), Buffer, DetectionStore, Incidents,
            Events, Realtime, Scenarios, View, NullLogger<GridDetectionEngine>.Instance);
    }

    public FakeTimeProvider Time { get; }
    public SecurityFakeGridView View { get; }
    public SecurityFakeGridTraining Training { get; }
    public SecurityFakeGridControl Control { get; }
    public RecentEventBuffer Buffer { get; }
    public RealtimeBroadcaster Realtime { get; }
    public EventPublisher Events { get; }
    public IncidentService Incidents { get; }
    public InMemoryIncidentStore IncidentStore { get; } = new();
    public InMemoryDetectionStore DetectionStore { get; }
    public InMemoryScenarioRunStore RunStore { get; }
    public GridDetectionEngine Detection { get; }
    public GridScenarioEngine Scenarios { get; }

    public static readonly Actor Instructor = Actor.System("instructor");
    public static readonly Actor Viewer = new("guest", AccessRole.Viewer);

    /// <summary>Steps the fake world once and notifies both grid observers, like the grid coordinator would.</summary>
    public async Task<GridStepReport> StepAsync(double seconds = 1, params GridStepEvent[] events)
    {
        Time.Advance(TimeSpan.FromSeconds(seconds));
        var report = View.Step(seconds, events);
        await Scenarios.OnGridStepAsync(report, CancellationToken.None);
        await Detection.OnGridStepAsync(report, CancellationToken.None);
        return report;
    }

    public async Task<int> RunForAsync(double simulatedSeconds)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(simulatedSeconds / View.StepSeconds));
        for (var i = 0; i < steps; i++)
            await StepAsync();
        return steps;
    }

    /// <summary>Runs the given scenario to completion by stepping until no run is active.</summary>
    public async Task<ScenarioRunRecord> RunScenarioAsync(string scenarioId, double maxSeconds = 600)
    {
        var start = await Scenarios.StartAsync(scenarioId, Instructor, CancellationToken.None);
        if (!start.Succeeded)
            throw new InvalidOperationException(start.Message);
        var steps = (int)(maxSeconds / View.StepSeconds) + 10;
        for (var i = 0; i < steps && Scenarios.ActiveRunId is not null; i++)
            await StepAsync();
        return Scenarios.Current ?? throw new InvalidOperationException("No scenario run recorded.");
    }

    public async Task<IReadOnlyList<DetectionRecord>> DetectionsAsync() =>
        await DetectionStore.QueryAsync(new DetectionQuery(Limit: 1000), CancellationToken.None);
}
