using Application.Identity;
using Domain.Grid.Commands;

namespace Application.Grid;

/// <summary>Lifecycle of the grid simulation: pause/resume (Operator), reset and loading a saved definition version (Administrator).</summary>
public interface IGridRuntime
{
    Task<GridCommandResult> SetPausedAsync(bool paused, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> ResetAsync(Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> ActivateVersionAsync(int version, Actor actor, CancellationToken cancellationToken);
}
