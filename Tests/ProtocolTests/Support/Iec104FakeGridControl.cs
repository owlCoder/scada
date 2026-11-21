using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace ProtocolTests.Support;

/// <summary>Fake <see cref="IGridControl"/> that records every command and returns a configurable result.</summary>
public sealed class Iec104FakeGridControl : IGridControl
{
    private readonly Lock _sync = new();
    private readonly List<(GridCommand Command, Actor Actor, string Source)> _commands = new();

    public GridCommandResult Result { get; set; } = GridCommandResult.Ok("accepted");

    public IReadOnlyList<(GridCommand Command, Actor Actor, string Source)> Commands
    {
        get { lock (_sync) return _commands.ToArray(); }
    }

    public Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken)
    {
        lock (_sync)
            _commands.Add((command, actor, source));
        return Task.FromResult(Result);
    }
}
