using Application.Commands;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Security.Grid.Control;

/// <summary>Shared authorization, replay/rate checks and atomic wall-clock select-before-operate.</summary>
public sealed class GridControlService(GridCoordinator coordinator, GridOptions options, TimeProvider time,
    ICommandRateLimiter rateLimiter, CommandReplayGuard replayGuard) : IGridControl
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
        else if (!rateLimiter.TryAcquire($"grid:{actor.Name}"))
            rejection = GridCommandResult.Reject(GridCommandRejection.RateLimited, "Command rate exceeded.");
        else if (command.SequenceNumber is { } sequence && !replayGuard.TryAccept(ReplayKey(actor.Name), sequence))
            rejection = GridCommandResult.Reject(GridCommandRejection.Replay, "Sequence is replayed or out of order.");
        if (rejection is not null)
            return Task.FromResult(coordinator.Audit(rejection, actor, source, $"{command.Type} {command.TargetId}"));
        return coordinator.ApplyAsync(w => w.ApplyCommand(command, actor.Name, time.GetUtcNow(), TimeSpan.FromSeconds(options.SelectTimeoutSeconds)),
            actor, source, false, cancellationToken, $"{command.Type} {command.TargetId}", command.TargetId);
    }
}
