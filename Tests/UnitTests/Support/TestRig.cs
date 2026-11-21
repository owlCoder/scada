using Application.Alarms;
using Application.Commands;
using Application.Events;
using Application.Incidents;
using Application.Registers;
using Application.Realtime;
using Application.Scenarios;
using Application.Simulation;
using Application.Telemetry;
using Application.Training;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Accounts;
using Security.Commands;
using Security.Detection;
using Security.Detection.Rules;
using Security.Scenarios;
using Security.Scenarios.Catalog;

namespace UnitTests.Support;

/// <summary>
/// The complete application graph with in-memory stores and a fake clock. Each step advances wall-clock time by
/// StepSeconds / TimeScale so event-window rules behave exactly as in the real-time loop, but deterministically.
/// </summary>
public sealed class TestRig
{
    public TestRig(RegisterWriteOptions? registerOptions = null, CommandRateOptions? rateOptions = null,
        Func<TimeProvider, IRealtimePublisher>? createRealtime = null)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        Topology = SimulationTopology.Default();
        Engine = new SimulationEngine(Topology, new SimulationOptions(), Time);
        Historian = new TelemetryHistorian();
        Buffer = new RecentEventBuffer();
        QueueOptions = new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero };
        Queue = new ChannelPersistenceQueue(QueueOptions, Time, NullLogger<ChannelPersistenceQueue>.Instance);
        var realtime = createRealtime?.Invoke(Time) ?? new NullRealtimePublisher();
        Events = new EventPublisher(Buffer, Queue, realtime);
        ReplayGuard = new CommandReplayGuard();
        Monitor = new CommandMonitor(Events, Time, rateOptions ?? new CommandRateOptions());
        Commands = new ControlCommandService(Engine, Monitor, ReplayGuard, Events, realtime, Time);
        RegisterOptions = registerOptions ?? new RegisterWriteOptions();
        Registers = new RegisterAccessService(Engine, RegisterOptions, Events, Time);
        Alarms = new AlarmService(Engine, Events, Queue, realtime, AlarmStore, Time);
        Incidents = new IncidentService(IncidentStore, Events, realtime, Time);
        Training = new TrainingEnvironment(Engine, Historian, Events, Time);
        Accounts = new UserAdministrationService(AccountStore, Events, Time);
        Scenarios = new ScenarioEngine(DefaultScenarios.Create(),
            new ScenarioServices(Training, Commands, ReplayGuard, Registers, Alarms, Accounts),
            Engine, DetectionStore, RunStore, Events, realtime, Time, NullLogger<ScenarioEngine>.Instance);
        Detection = new DetectionEngine(DefaultDetectionRules.Create(Topology, Historian), Buffer, DetectionStore, Incidents,
            Events, realtime, Scenarios, Engine, NullLogger<DetectionEngine>.Instance);
        Coordinator = new SimulationCoordinator(Engine, Events, Historian, Queue, realtime, [Scenarios, Detection], Time,
            NullLogger<SimulationCoordinator>.Instance);
        Worker = new PersistenceWorker(Queue, new PersistenceDispatcher(EventStore, TelemetryStore, AlarmStore, PlantStateStore, realtime),
            QueueOptions, Time, NullLogger<PersistenceWorker>.Instance);
    }

    public FakeTimeProvider Time { get; }
    public SimulationTopology Topology { get; }
    public SimulationEngine Engine { get; }
    public TelemetryHistorian Historian { get; }
    public RecentEventBuffer Buffer { get; }
    public PersistenceQueueOptions QueueOptions { get; }
    public ChannelPersistenceQueue Queue { get; }
    public EventPublisher Events { get; }
    public CommandReplayGuard ReplayGuard { get; }
    public CommandMonitor Monitor { get; }
    public ControlCommandService Commands { get; }
    public RegisterWriteOptions RegisterOptions { get; }
    public RegisterAccessService Registers { get; }
    public AlarmService Alarms { get; }
    public IncidentService Incidents { get; }
    public ITrainingEnvironment Training { get; }
    public UserAdministrationService Accounts { get; }
    public ScenarioEngine Scenarios { get; }
    public DetectionEngine Detection { get; }
    public SimulationCoordinator Coordinator { get; }
    public PersistenceWorker Worker { get; }
    public InMemoryEventStore EventStore { get; } = new();
    public InMemoryTelemetryStore TelemetryStore { get; } = new();
    public InMemoryAlarmHistoryStore AlarmStore { get; } = new();
    public InMemoryPlantStateStore PlantStateStore { get; } = new();
    public InMemoryIncidentStore IncidentStore { get; } = new();
    public InMemoryDetectionStore DetectionStore { get; } = new();
    public InMemoryScenarioRunStore RunStore { get; } = new();
    public InMemoryUserAccountStore AccountStore { get; } = new();

    public TimeSpan WallStep => TimeSpan.FromSeconds(Engine.Options.StepSeconds / Engine.Options.TimeScale);

    public async Task<int> StepAsync(int count = 1)
    {
        var executed = 0;
        for (var i = 0; i < count; i++)
        {
            Time.Advance(WallStep);
            if (await Coordinator.StepAsync(true, CancellationToken.None) is not null)
                executed++;
            if (i % 50 == 49)
                await Worker.DrainAsync(CancellationToken.None);
        }

        await Worker.DrainAsync(CancellationToken.None);
        return executed;
    }

    public Task<int> RunForAsync(double simulatedSeconds) =>
        StepAsync((int)Math.Ceiling(simulatedSeconds / Engine.Options.StepSeconds));

    /// <summary>Starts a scenario and steps until it has finished. Returns the final run record.</summary>
    public async Task<ScenarioRunRecord> RunScenarioAsync(string scenarioId)
    {
        var start = await Scenarios.StartAsync(scenarioId, global::Application.Identity.Actor.System("instructor"), CancellationToken.None);
        if (!start.Succeeded)
            throw new InvalidOperationException(start.Message);

        var maxSteps = (int)(start.Run!.Duration.TotalSeconds / Engine.Options.StepSeconds) + 10;
        for (var i = 0; i < maxSteps && Scenarios.ActiveRunId is not null; i++)
            await StepAsync();
        return Scenarios.Current ?? throw new InvalidOperationException("No scenario run recorded.");
    }
}
