using Application.Identity;
using Domain.Grid.Commands;

namespace Application.Grid;

/// <summary>Placing and removing tags requires the Operator role; every change is audited and published on
/// <c>RealtimeTopics.GridTags</c>. Tags are cleared when a different network definition is activated.</summary>
public interface IGridTagging
{
    IReadOnlyList<GridTag> Tags { get; }
    Task<GridCommandResult> PlaceTagAsync(string elementId, GridTagKind kind, string reason, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> RemoveTagAsync(string tagId, Actor actor, CancellationToken cancellationToken);
}
