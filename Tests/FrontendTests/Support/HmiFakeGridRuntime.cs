using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace FrontendTests.Support;

public sealed class HmiFakeGridRuntime : IGridRuntime
{
    public List<(int Version, Actor Actor)> Activations { get; } = [];
    public Task<GridCommandResult> ActivateVersionAsync(int version, Actor actor, CancellationToken cancellationToken)
    {
        Activations.Add((version, actor));
        return Task.FromResult(GridCommandResult.Ok("Version activated."));
    }
    public Task<GridCommandResult> SetPausedAsync(bool paused, Actor actor, CancellationToken cancellationToken) => Task.FromResult(GridCommandResult.Ok("Paused."));
    public Task<GridCommandResult> ResetAsync(Actor actor, CancellationToken cancellationToken) => Task.FromResult(GridCommandResult.Ok("Reset."));
}
