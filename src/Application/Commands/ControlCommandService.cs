using Application.Events;
using Application.Identity;
using Application.Realtime;
using Application.Simulation;
using Domain.Process.Commands;
using Domain.Process.Devices;

namespace Application.Commands;

/// <summary>
/// Single entry point for supervisory control. Every request is authorised, rate limited, checked for
/// replay and communication availability, executed against the plant and audited, whatever its source.
/// </summary>
public sealed class ControlCommandService(
    SimulationEngine engine,
    ICommandRateLimiter rateLimiter,
    CommandReplayGuard replayGuard,
    IEventPublisher events,
    IRealtimePublisher realtime,
    TimeProvider time)
{
    public CommandResult Execute(OperatorCommand command, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var deviceId = command.DeviceId?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(actor.Name))
            return Reject(command, actor, CommandResult.Rejected(deviceId, CommandRejectionReason.NotAuthorized,
                "An authenticated actor is required."), EventCategories.AuthorizationDenied, EventSeverity.Medium);

        if (!actor.HasRole(AccessRole.Operator))
            return Reject(command, actor, CommandResult.Rejected(deviceId, CommandRejectionReason.NotAuthorized,
                $"Role {actor.Role} may not issue control commands."), EventCategories.AuthorizationDenied, EventSeverity.High);

        if (!Enum.IsDefined(command.Type) || deviceId.Length is 0 or > 32)
            return Reject(command, actor, CommandResult.Rejected(deviceId, CommandRejectionReason.InvalidValue,
                "Invalid command."), EventCategories.CommandRejected, EventSeverity.Low);

        if (!rateLimiter.TryAcquire(actor.Name))
            return Reject(command, actor, CommandResult.Rejected(deviceId, CommandRejectionReason.RateLimited,
                "Too many commands in a short period."), EventCategories.CommandRejected, EventSeverity.Medium);

        if (command.SequenceNumber is { } sequence && !replayGuard.TryAccept(actor.Name, sequence))
            return Reject(command, actor, CommandResult.Rejected(deviceId, CommandRejectionReason.ReplayDetected,
                $"Sequence number {sequence} was already used or is out of order."), EventCategories.SecurityAlert, EventSeverity.High);

        var path = engine.CheckCommandPath(deviceId);
        if (!path.Accepted)
            return Reject(command, actor, path,
                path.Reason == CommandRejectionReason.UnknownDevice ? EventCategories.CommandRejected : EventCategories.Communication,
                EventSeverity.Medium);

        var origin = CommandOrigin.Operator(actor.Name);
        var result = engine.Mutate(world => command.Type switch
        {
            OperatorCommandType.StartPump => world.Plant.CommandPump(deviceId, true, origin),
            OperatorCommandType.StopPump => world.Plant.CommandPump(deviceId, false, origin),
            OperatorCommandType.SetValvePosition => world.Plant.CommandValve(deviceId, command.Value ?? double.NaN, origin),
            OperatorCommandType.SetAutomatic => world.Plant.SetMode(deviceId, ControlMode.Automatic, origin),
            OperatorCommandType.SetManual => world.Plant.SetMode(deviceId, ControlMode.Manual, origin),
            OperatorCommandType.ResetInterlock => world.Plant.ResetInterlock(deviceId),
            _ => CommandResult.Rejected(deviceId, CommandRejectionReason.InvalidValue, "Unsupported command.")
        });

        if (!result.Accepted)
            return Reject(command, actor, result, EventCategories.CommandRejected, EventSeverity.Low);

        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information,
            command.Source.ToString(), $"{Describe(command)}: {result.Message}", actor.Name, deviceId));
        realtime.Publish(RealtimeTopics.Plant, engine.Read(world => world.Plant.CaptureSnapshot()));
        return result;
    }

    private CommandResult Reject(OperatorCommand command, Actor actor, CommandResult result, string category, EventSeverity severity)
    {
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), category, severity, command.Source.ToString(),
            $"{Describe(command)} rejected ({result.Reason}): {result.Message}",
            string.IsNullOrWhiteSpace(actor.Name) ? "anonymous" : actor.Name, result.DeviceId));
        return result;
    }

    private static string Describe(OperatorCommand command) => command.Type switch
    {
        OperatorCommandType.SetValvePosition => $"Set {command.DeviceId} to {command.Value:0.#}%",
        _ => $"{command.Type} {command.DeviceId}"
    };
}
