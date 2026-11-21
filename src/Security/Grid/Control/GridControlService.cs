using Application.Commands;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Security.Grid.Control;

/// <summary>Shared authorization, replay/rate checks and atomic wall-clock select-before-operate.
/// Dispatcher tags are enforced here: switching commands (SelectOpen/SelectClose/Execute) on a switch that is tagged
/// with a blocking kind, or whose element (line/transformer/load) carries such a tag, and tap/capacitor/load commands
/// on a tagged element are rejected with <see cref="GridCommandRejection.Interlocked"/>. Information tags never block.
/// Training actions (<see cref="IGridTraining"/>, e.g. ForceSwitch) deliberately bypass tags so attack scenarios can
/// still override them. IEC 60870-5-104 commands go through this service, so they are blocked as well.</summary>
public sealed class GridControlService(GridCoordinator coordinator, GridOptions options, TimeProvider time,
    ICommandRateLimiter rateLimiter, CommandReplayGuard replayGuard, IGridTagging tagging, IGridView view) : IGridControl
{
    /// <summary>The replay-guard key for a grid actor (grid commands share one guard with a grid-namespaced key).</summary>
    public static string ReplayKey(string actor) => $"grid:{actor}";

    public Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        GridCommandResult? rejection = null;
        if (string.IsNullOrWhiteSpace(actor.Name) || !actor.HasRole(AccessRole.Operator))
            rejection = GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Operator role required.");
        else if (!Enum.IsDefined(command.Type) || string.IsNullOrWhiteSpace(command.TargetId) || command.TargetId.Length > 128)
            rejection = GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Invalid command.");
        else if (TagBlock(command) is { } blocked)
            rejection = blocked;
        else if (!rateLimiter.TryAcquire($"grid:{actor.Name}"))
            rejection = GridCommandResult.Reject(GridCommandRejection.RateLimited, "Command rate exceeded.");
        else if (command.SequenceNumber is { } sequence && !replayGuard.TryAccept(ReplayKey(actor.Name), sequence))
            rejection = GridCommandResult.Reject(GridCommandRejection.Replay, "Sequence is replayed or out of order.");
        if (rejection is not null)
            return Task.FromResult(coordinator.Audit(rejection, actor, source, $"{command.Type} {command.TargetId}"));
        return coordinator.ApplyAsync(w => w.ApplyCommand(command, actor.Name, time.GetUtcNow(), TimeSpan.FromSeconds(options.SelectTimeoutSeconds)),
            actor, source, false, cancellationToken, $"{command.Type} {command.TargetId}", command.TargetId);
    }

    /// <summary>Rejects commands on elements carrying a blocking tag (DoNotOperate/Earthed/WorkInProgress): the tagged
    /// element itself, or — for switch commands — the element the switch connects to its bus.</summary>
    private GridCommandResult? TagBlock(GridCommand command)
    {
        if (!IsTagEnforced(command.Type)) return null;
        var blocking = tagging.Tags.Where(t => GridTaggingService.BlocksOperations(t.Kind)).ToArray();
        if (blocking.Length == 0) return null;
        var elementId = command.Type is GridCommandType.SelectOpen or GridCommandType.SelectClose or GridCommandType.Execute
            ? view.Definition.Switches.FirstOrDefault(s => s.Id == command.TargetId)?.ElementId
            : null;
        var tag = blocking.FirstOrDefault(t => t.ElementId == command.TargetId)
            ?? (elementId is null ? null : blocking.FirstOrDefault(t => t.ElementId == elementId));
        return tag is null ? null : GridCommandResult.Reject(GridCommandRejection.Interlocked,
            $"Blocked by {tag.Kind} tag {tag.Id} on {tag.ElementId} ({tag.Reason}).");
    }

    private static bool IsTagEnforced(GridCommandType type) => type is GridCommandType.SelectOpen or GridCommandType.SelectClose
        or GridCommandType.Execute or GridCommandType.TapRaise or GridCommandType.TapLower
        or GridCommandType.SetTapAutomatic or GridCommandType.SetTapManual or GridCommandType.SetCapacitorSteps
        or GridCommandType.ShedLoad or GridCommandType.RestoreLoad;
}
