using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace FrontendTests.Support;

/// <summary>In-memory tagging fake for workspace tests: records calls, enforces the Operator role like the real service.</summary>
public sealed class HmiWorkspaceFakeGridTagging : IGridTagging
{
    private readonly List<GridTag> _tags = [];
    private int _nextId;

    public List<(string ElementId, GridTagKind Kind, string Reason, Actor Actor)> PlaceCalls { get; } = [];
    public List<(string TagId, Actor Actor)> RemoveCalls { get; } = [];

    public IReadOnlyList<GridTag> Tags => _tags;

    public Task<GridCommandResult> PlaceTagAsync(string elementId, GridTagKind kind, string reason, Actor actor, CancellationToken cancellationToken)
    {
        PlaceCalls.Add((elementId, kind, reason, actor));
        if (!actor.HasRole(AccessRole.Operator))
            return Task.FromResult(GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Placing tags requires Operator."));
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(GridCommandResult.Reject(GridCommandRejection.InvalidValue, "A reason is required."));
        var tag = new GridTag($"TAG{++_nextId}", elementId, kind, reason.Trim(), actor.Name, DateTimeOffset.UtcNow);
        _tags.Add(tag);
        return Task.FromResult(GridCommandResult.Ok("Tag placed."));
    }

    public Task<GridCommandResult> RemoveTagAsync(string tagId, Actor actor, CancellationToken cancellationToken)
    {
        RemoveCalls.Add((tagId, actor));
        if (!actor.HasRole(AccessRole.Operator))
            return Task.FromResult(GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Removing tags requires Operator."));
        var tag = _tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is null)
            return Task.FromResult(GridCommandResult.Reject(GridCommandRejection.UnknownTarget, "Unknown tag."));
        _tags.Remove(tag);
        return Task.FromResult(GridCommandResult.Ok("Tag removed."));
    }
}
