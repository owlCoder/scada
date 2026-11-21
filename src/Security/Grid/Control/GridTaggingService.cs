using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Realtime;
using Domain.Grid.Commands;
using Domain.Grid.Definition;

namespace Security.Grid.Control;

/// <summary>
/// Operator-placed dispatcher tags (hanging tags / earthing points in control-room practice) on grid elements.
/// Placing and removing requires the Operator role, is audited (<c>grid:tags</c>) and published on
/// <see cref="RealtimeTopics.GridTags"/>. Tags are cleared when a different definition is activated.
/// <see cref="GridTagKind.DoNotOperate"/>, <see cref="GridTagKind.Earthed"/> and <see cref="GridTagKind.WorkInProgress"/>
/// are enforced by <see cref="GridControlService"/> (see <see cref="BlocksOperations"/>); training actions such as
/// <c>IGridTraining.ForceSwitchAsync</c> deliberately ignore tags so attack scenarios can still override them.
/// </summary>
public sealed class GridTaggingService(IGridView view, IEventPublisher events, IRealtimePublisher realtime, TimeProvider time)
    : IGridTagging, IGridObserver
{
    public const int MaxTags = 200;
    public const int MaxReasonLength = 200;
    public const string Source = "grid:tags";

    private readonly Lock _sync = new();
    private readonly Dictionary<string, GridTag> _tags = new(StringComparer.Ordinal);
    private GridDefinition? _definition;

    /// <summary>True for the tag kinds that block supervisory switching of the tagged element (and of switches whose
    /// element carries such a tag). <see cref="GridTagKind.Information"/> never blocks.</summary>
    public static bool BlocksOperations(GridTagKind kind) =>
        kind is GridTagKind.DoNotOperate or GridTagKind.Earthed or GridTagKind.WorkInProgress;

    public IReadOnlyList<GridTag> Tags
    {
        get { lock (_sync) { SyncDefinition(); return [.. _tags.Values]; } }
    }

    public Task<GridCommandResult> PlaceTagAsync(string elementId, GridTagKind kind, string reason, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actor.Name) || !actor.HasRole(AccessRole.Operator))
            return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Operator role required.")));
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(elementId) || elementId.Length > 128)
            return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Invalid tag.")));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
            return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.InvalidValue,
                $"A reason is required (max {MaxReasonLength} characters).")));
        lock (_sync)
        {
            SyncDefinition();
            if (!ElementExists(view.Definition, elementId))
                return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.UnknownTarget,
                    "Element is not part of the active definition.")));
            if (_tags.Values.Any(t => t.ElementId == elementId && t.Kind == kind))
                return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.AlreadyInState,
                    $"Element already carries a {kind} tag.")));
            if (_tags.Count >= MaxTags)
                return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.InvalidValue,
                    $"Tag limit of {MaxTags} reached.")));
            var tag = new GridTag(NewId(), elementId, kind, reason.Trim(), actor.Name, time.GetUtcNow());
            _tags[tag.Id] = tag;
            Publish();
            return Task.FromResult(Accepted(actor, $"Tag {tag.Id} ({kind}) placed on {elementId}: {tag.Reason}"));
        }
    }

    public Task<GridCommandResult> RemoveTagAsync(string tagId, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actor.Name) || !actor.HasRole(AccessRole.Operator))
            return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Operator role required.")));
        if (string.IsNullOrWhiteSpace(tagId) || tagId.Length > 128)
            return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Invalid tag id.")));
        lock (_sync)
        {
            SyncDefinition();
            if (!_tags.Remove(tagId, out var tag))
                return Task.FromResult(Rejected(actor, GridCommandResult.Reject(GridCommandRejection.UnknownTarget, "Unknown tag.")));
            Publish();
            return Task.FromResult(Accepted(actor, $"Tag {tag.Id} ({tag.Kind}) on {tag.ElementId} removed."));
        }
    }

    /// <summary>Clears the tags when the active definition changed (activation publishes the empty list).</summary>
    public ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (SyncDefinition()) Publish();
        }
        return new ValueTask<int>(0);
    }

    /// <summary>Must be called under <see cref="_sync"/>. Returns true when tags were cleared because the active
    /// definition changed (the caller then republishes the tag list).</summary>
    private bool SyncDefinition()
    {
        var current = view.Definition;
        if (ReferenceEquals(_definition, current)) return false;
        var hadTags = _tags.Count > 0;
        _definition = current;
        _tags.Clear();
        return hadTags;
    }

    private bool Publish() => realtime.Publish(RealtimeTopics.GridTags, _tags.Values.ToArray());

    private string NewId()
    {
        string id;
        do { id = Guid.NewGuid().ToString("N"); } while (_tags.ContainsKey(id));
        return id;
    }

    private GridCommandResult Accepted(Actor actor, string message)
    {
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information, Source, message, actor.Name));
        return GridCommandResult.Ok(message);
    }

    private GridCommandResult Rejected(Actor actor, GridCommandResult result)
    {
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Medium, Source,
            $"Tag rejected: {result.Reason}: {result.Message}", actor.Name));
        return result;
    }

    private static bool ElementExists(GridDefinition definition, string elementId) =>
        definition.Buses.Any(e => e.Id == elementId) || definition.Sources.Any(e => e.Id == elementId)
        || definition.Lines.Any(e => e.Id == elementId) || definition.Transformers.Any(e => e.Id == elementId)
        || definition.Switches.Any(e => e.Id == elementId) || definition.Loads.Any(e => e.Id == elementId)
        || definition.Generators.Any(e => e.Id == elementId) || definition.Capacitors.Any(e => e.Id == elementId)
        || definition.Protections.Any(e => e.Id == elementId) || definition.Rtus.Any(e => e.Id == elementId);
}
