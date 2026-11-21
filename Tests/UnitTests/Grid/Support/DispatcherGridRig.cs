using Application.Commands;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Realtime;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Infrastructure.Persistence.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Commands;
using Security.Grid.Control;
using UnitTests.Support;

namespace UnitTests.Grid.Support;

/// <summary>
/// Wires the real dispatcher backend — <see cref="GridEngine"/>, <see cref="GridCoordinator"/>,
/// <see cref="GridTaggingService"/>, <see cref="GridHistoryService"/>, <see cref="GridControlService"/> and
/// <see cref="GridTrainingService"/> — with a fake clock and recording publishers, so tests can drive steps and
/// commands deterministically.
/// </summary>
public sealed class DispatcherGridRig
{
    public DispatcherGridRig(GridOptions? options = null, GridDefinition? definition = null)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 2, 2, 8, 0, 0, TimeSpan.Zero));
        Options = options ?? new();
        Engine = definition is null ? new(Options, Time) : new(definition, Options, Time);
        Store = new InMemoryGridDefinitionStore(Time);
        Realtime = new GridEngineRealtimeCollector();
        Events = new GridEngineEventCollector();
        Tagging = new(Engine, Events, Realtime, Time);
        History = new(Engine, Options);
        var observers = new List<IGridObserver> { Tagging, History };
        Coordinator = new(Engine, Store, Events, Realtime, observers, Time, NullLogger<GridCoordinator>.Instance);
        Control = new(Coordinator, Options, Time, new CommandMonitor(Events, Time, new CommandRateOptions { Limit = 100_000 }),
            new CommandReplayGuard(), Tagging, Engine);
        Training = new(Coordinator);
    }

    public FakeTimeProvider Time { get; }
    public GridOptions Options { get; }
    public GridEngine Engine { get; }
    public InMemoryGridDefinitionStore Store { get; }
    public GridEngineRealtimeCollector Realtime { get; }
    public GridEngineEventCollector Events { get; }
    public GridTaggingService Tagging { get; }
    public GridHistoryService History { get; }
    public GridCoordinator Coordinator { get; }
    public GridControlService Control { get; }
    public GridTrainingService Training { get; }

    public static Actor Operator { get; } = new("ana", AccessRole.Operator);
    public static Actor Viewer { get; } = new("vic", AccessRole.Viewer);
    public static Actor Instructor { get; } = Actor.System("instructor");

    /// <summary>Advances fake time by one step and runs the coordinator step, notifying all grid observers.</summary>
    public async Task<GridStepReport?> StepAsync()
    {
        Time.Advance(TimeSpan.FromSeconds(Options.StepSeconds));
        return await Coordinator.StepAsync(false, CancellationToken.None);
    }

    public Task<GridCommandResult> Command(GridCommandType type, string id, double? value = null, Actor? actor = null) =>
        Control.ExecuteAsync(new(type, id, value), actor ?? Operator, "test", CancellationToken.None);
}
