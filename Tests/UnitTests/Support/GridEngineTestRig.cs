using Application.Commands;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Commands;
using Security.Grid.Control;

namespace UnitTests.Support;

public sealed class GridEngineTestRig
{
    public GridEngineTestRig(GridOptions? options = null, IEnumerable<IGridObserver>? observers = null, CommandRateOptions? rate = null)
    {
        Options = options ?? new();
        Engine = new(Options, Time);
        Store = new(Time);
        Coordinator = new(Engine, Store, Events, new NullRealtimePublisher(), observers ?? [], Time, NullLogger<GridCoordinator>.Instance);
        Control = new(Coordinator, Options, Time, new CommandMonitor(Events, Time, rate ?? new() { Limit = 1000 }), new());
        Training = new(Coordinator);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
    public GridOptions Options { get; }
    public GridEngine Engine { get; }
    public InMemoryGridDefinitionStore Store { get; }
    public GridEngineEventCollector Events { get; } = new();
    public GridCoordinator Coordinator { get; }
    public GridControlService Control { get; }
    public GridTrainingService Training { get; }
    public static Actor Operator { get; } = new("ana", AccessRole.Operator);
    public static Actor Administrator { get; } = Actor.System("instructor");
    public Task<GridCommandResult> Command(GridCommandType type, string id, double? value = null, Actor? actor = null, long? sequence = null) =>
        Control.ExecuteAsync(new(type, id, value, sequence), actor ?? Operator, "test", CancellationToken.None);
}
