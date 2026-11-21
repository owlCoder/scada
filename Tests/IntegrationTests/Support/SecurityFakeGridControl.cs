using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace IntegrationTests.Support;

/// <summary>Minimal <see cref="IGridControl"/> for API-level grid security tests: role check, replay guard, and events for accepted/rejected commands.</summary>
public sealed class SecurityFakeGridControl(SecurityFakeGridView view) : IGridControl
{
    private readonly Dictionary<string, long> _lastSequence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _selectedToClose = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken)
    {
        GridCommandResult result;
        if (!actor.HasRole(AccessRole.Operator))
        {
            result = GridCommandResult.Reject(GridCommandRejection.NotAuthorized, $"NotAuthorized: role {actor.Role} may not issue grid commands.");
        }
        else if (command.SequenceNumber is { } seq && _lastSequence.TryGetValue(actor.Name, out var last) && seq <= last && !(seq == 1 && last > 1))
        {
            result = GridCommandResult.Reject(GridCommandRejection.Replay, $"Replay: sequence number {seq} was already used by {actor.Name}.");
        }
        else
        {
            result = Apply(command, actor, source);
        }

        if (command.SequenceNumber is { } s)
        {
            var last = _lastSequence.GetValueOrDefault(actor.Name);
            _lastSequence[actor.Name] = s == 1 && last > 1 ? 1 : Math.Max(last, s);
        }

        _calls.Add($"{actor.Name}({actor.Role}) {source} {command.Type} {command.TargetId} seq={command.SequenceNumber} -> {result.Reason}");
        view.Emit(result.Accepted
            ? new GridStepEvent(GridStepEventKind.CommandAccepted, command.TargetId, result.Message, actor.Name, Domain.Grid.Snapshots.GridChangeCause.Operator, source)
            : new GridStepEvent(GridStepEventKind.CommandRejected, command.TargetId, result.Message, actor.Name, Domain.Grid.Snapshots.GridChangeCause.Operator, source));
        return Task.FromResult(result);
    }

    private GridCommandResult Apply(GridCommand command, Actor actor, string source)
    {
        switch (command.Type)
        {
            case GridCommandType.SelectOpen:
            case GridCommandType.SelectClose:
                _selectedToClose[command.TargetId] = command.Type == GridCommandType.SelectClose;
                return GridCommandResult.Ok($"Switch {command.TargetId} selected.");
            case GridCommandType.Cancel:
                _selectedToClose.Remove(command.TargetId);
                return GridCommandResult.Ok($"Selection on {command.TargetId} cancelled.");
            case GridCommandType.Execute:
                if (!_selectedToClose.TryGetValue(command.TargetId, out var close))
                    return GridCommandResult.Reject(GridCommandRejection.NotSelected, $"NotSelected: {command.TargetId} was not selected.");
                _selectedToClose.Remove(command.TargetId);
                view.SetBreakerClosed(close);
                view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, command.TargetId,
                    $"Breaker {(close ? "closed" : "opened")} by operator command", actor.Name, Domain.Grid.Snapshots.GridChangeCause.Operator, source));
                return GridCommandResult.Ok($"Switch {command.TargetId} operated.");
            default:
                return GridCommandResult.Ok($"{command.Type} on {command.TargetId} accepted.");
        }
    }
}
