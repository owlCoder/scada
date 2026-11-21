using Application.Commands;
using Application.Identity;
using Domain.Process.Commands;
using Opc.Ua;

namespace Protocols.OpcUa.Nodes;

internal sealed class PlantCommandMethod(NodeState parent, string deviceId, OperatorCommandType command, ControlCommandService commands)
    : MethodState(parent)
{
    // The SDK normally exits before invoking the method when UserExecutable is false.
    // Route the rejected request through the command service so it is audited there.
    protected override ValueTask<ServiceResult> CallInternalSyncOrAsync(
        ISystemContext context, NodeId objectId, IList<Variant> inputArguments, IList<ServiceResult> argumentErrors,
        IList<Variant> outputArguments, bool sync, CancellationToken cancellationToken = default)
    {
        var actor = OpcUaSessionIdentity.ActorFor(context);
        if (!actor.HasRole(AccessRole.Operator))
            return ValueTask.FromResult(Execute(actor, null));

        return base.CallInternalSyncOrAsync(context, objectId, inputArguments, argumentErrors, outputArguments, sync, cancellationToken);
    }

    public ServiceResult ReadUserExecutable(ISystemContext context, NodeState node, ref bool executable)
    {
        executable = OpcUaSessionIdentity.ActorFor(context).HasRole(AccessRole.Operator);
        return ServiceResult.Good;
    }

    public ServiceResult Invoke(ISystemContext context, MethodState method, IList<object> inputs, IList<object> outputs) =>
        Execute(OpcUaSessionIdentity.ActorFor(context), inputs.Count == 1 && inputs[0] is double value ? value : null);

    private ServiceResult Execute(Actor actor, double? value)
    {
        var result = commands.Execute(new OperatorCommand(deviceId, command, value, CommandSource.OpcUa), actor);
        return result.Reason switch
        {
            CommandRejectionReason.None => ServiceResult.Good,
            CommandRejectionReason.NotAuthorized => StatusCodes.BadUserAccessDenied,
            CommandRejectionReason.InvalidValue => StatusCodes.BadOutOfRange,
            CommandRejectionReason.RateLimited => StatusCodes.BadTooManyOperations,
            CommandRejectionReason.CommunicationUnavailable => StatusCodes.BadNoCommunication,
            CommandRejectionReason.UnknownDevice => StatusCodes.BadNodeIdUnknown,
            _ => StatusCodes.BadInvalidState
        };
    }
}
