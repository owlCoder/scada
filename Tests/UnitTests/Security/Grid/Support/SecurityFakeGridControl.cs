using Application.Events;
using global::Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace UnitTests.Security.Grid.Support;

/// <summary>
/// Fake <see cref="IGridControl"/> implementing the platform guards an attacker would hit: role check (Operator
/// required), select-before-operate, sequence-number replay guard and a per-actor rate limit. Accepted commands are
/// applied to the <see cref="SecurityFakeGridView"/> and raise the same <see cref="GridStepEvent"/>s a real engine would
/// (CommandAccepted plus the operation event with cause Operator and the command's channel). Rejections raise
/// CommandRejected events whose message names the reason.
/// </summary>
public sealed class SecurityFakeGridControl(SecurityFakeGridView view, IEventPublisher? events = null, int rateLimit = 100) : IGridControl
{
    private readonly List<(GridCommand Command, string Actor, string Source, GridCommandResult Result)> _commands = [];
    private readonly Dictionary<string, long> _lastSequence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _recentCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _selectedToClose = new(StringComparer.Ordinal);

    public IReadOnlyList<(GridCommand Command, string Actor, string Source, GridCommandResult Result)> Commands => _commands;

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
        else if (_recentCounts.GetValueOrDefault(actor.Name) >= rateLimit)
        {
            result = GridCommandResult.Reject(GridCommandRejection.RateLimited, $"RateLimited: {actor.Name} exceeded the command rate limit.");
        }
        else
        {
            result = Apply(command, actor, source);
        }

        if (command.SequenceNumber is { } s)
        {
            var last = _lastSequence.GetValueOrDefault(actor.Name);
            // Sequence 1 after higher numbers models a new protocol session (connection restart), not a replay.
            _lastSequence[actor.Name] = s == 1 && last > 1 ? 1 : Math.Max(last, s);
        }
        _recentCounts[actor.Name] = _recentCounts.GetValueOrDefault(actor.Name) + 1;

        _commands.Add((command, actor.Name, source, result));
        view.Emit(result.Accepted
            ? new GridStepEvent(GridStepEventKind.CommandAccepted, command.TargetId, result.Message, actor.Name, GridChangeCause.Operator, source)
            : new GridStepEvent(GridStepEventKind.CommandRejected, command.TargetId, result.Message, actor.Name, GridChangeCause.Operator, source));
        events?.Publish(SecurityEvent.Create(view.Now, EventCategories.CommandRejected, EventSeverity.Medium, "grid-control",
            result.Message, actor.Name, command.TargetId));
        return Task.FromResult(result);
    }

    private GridCommandResult Apply(GridCommand command, Actor actor, string source)
    {
        var target = command.TargetId;
        switch (command.Type)
        {
            case GridCommandType.SelectOpen:
            case GridCommandType.SelectClose:
                if (!view.Definition.Switches.Any(s => s.Id == target))
                    return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no switch {target}.");
                _selectedToClose[target] = command.Type == GridCommandType.SelectClose;
                return GridCommandResult.Ok($"Switch {target} selected to {(command.Type == GridCommandType.SelectClose ? "close" : "open")}.");
            case GridCommandType.Cancel:
                _selectedToClose.Remove(target);
                return GridCommandResult.Ok($"Selection on {target} cancelled.");
            case GridCommandType.Execute:
                {
                    if (!view.Definition.Switches.Any(s => s.Id == target))
                        return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no switch {target}.");
                    if (!_selectedToClose.TryGetValue(target, out var close))
                        return GridCommandResult.Reject(GridCommandRejection.NotSelected, $"NotSelected: {target} was not selected.");
                    _selectedToClose.Remove(target);
                    var name = view.Definition.Switches.First(s => s.Id == target).Name;
                    view.SetSwitchState(target, close, GridChangeCause.Operator, actor.Name);
                    view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, target,
                        $"{name} {(close ? "closed" : "opened")} by operator command", actor.Name, GridChangeCause.Operator, source));
                    return GridCommandResult.Ok($"Switch {target} operated.");
                }
            case GridCommandType.TapRaise:
            case GridCommandType.TapLower:
                {
                    var transformer = view.Definition.Transformers.FirstOrDefault(t => t.Id == target);
                    if (transformer is null)
                        return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no transformer {target}.");
                    var current = CurrentTap(transformer);
                    var next = command.Type == GridCommandType.TapRaise ? Math.Min(transformer.TapMax, current + 1) : Math.Max(transformer.TapMin, current - 1);
                    view.SetTap(target, next, automatic: false);
                    view.Emit(new GridStepEvent(GridStepEventKind.TapChanged, target, $"Tap of {target} set to {next} by operator", actor.Name, GridChangeCause.Operator, source));
                    return GridCommandResult.Ok($"Tap of {target} set to {next}.");
                }
            case GridCommandType.SetTapAutomatic:
            case GridCommandType.SetTapManual:
                {
                    var transformer = view.Definition.Transformers.FirstOrDefault(t => t.Id == target);
                    if (transformer is null)
                        return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no transformer {target}.");
                    view.SetTap(target, CurrentTap(transformer), automatic: command.Type == GridCommandType.SetTapAutomatic);
                    return GridCommandResult.Ok($"Tap mode of {target} set to {(command.Type == GridCommandType.SetTapAutomatic ? "automatic" : "manual")}.");
                }
            case GridCommandType.SetCapacitorSteps:
                {
                    var capacitor = view.Definition.Capacitors.FirstOrDefault(c => c.Id == target);
                    if (capacitor is null)
                        return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no capacitor {target}.");
                    var steps = (int)(command.Value ?? 0);
                    if (steps < 0 || steps > capacitor.Steps)
                        return GridCommandResult.Reject(GridCommandRejection.InvalidValue, $"InvalidValue: steps {steps} outside 0..{capacitor.Steps}.");
                    view.SetCapacitorSteps(target, steps);
                    view.Emit(new GridStepEvent(GridStepEventKind.CapacitorChanged, target,
                        $"{capacitor.Name} set to {steps}/{capacitor.Steps} steps by operator", actor.Name, GridChangeCause.Operator, source));
                    return GridCommandResult.Ok($"Capacitor {target} set to {steps} steps.");
                }
            case GridCommandType.ShedLoad:
            case GridCommandType.RestoreLoad:
                {
                    var load = view.Definition.Loads.FirstOrDefault(l => l.Id == target);
                    if (load is null)
                        return GridCommandResult.Reject(GridCommandRejection.UnknownTarget, $"UnknownTarget: no load {target}.");
                    var shed = command.Type == GridCommandType.ShedLoad;
                    view.SetShed(target, shed);
                    view.Emit(new GridStepEvent(shed ? GridStepEventKind.LoadShed : GridStepEventKind.LoadRestored, target,
                        $"{load.Name} {(shed ? "shed" : "restored")} by command from {actor.Name}", actor.Name, GridChangeCause.Operator, source));
                    return GridCommandResult.Ok($"Load {target} {(shed ? "shed" : "restored")}.");
                }
            case GridCommandType.ResetProtection:
            case GridCommandType.AcknowledgeAlarm:
                return GridCommandResult.Ok($"{command.Type} on {target} acknowledged.");
            default:
                return GridCommandResult.Reject(GridCommandRejection.InvalidValue, $"InvalidValue: unsupported command {command.Type}.");
        }
    }

    private int CurrentTap(global::Domain.Grid.Definition.GridTransformerDefinition transformer) =>
        view.Snapshot().Branch(transformer.Id)?.TapPosition ?? transformer.InitialTap;
}
