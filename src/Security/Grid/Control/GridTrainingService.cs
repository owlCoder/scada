using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;

namespace Security.Grid.Control;

public sealed class GridTrainingService(GridCoordinator coordinator) : IGridTraining
{
    public Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetFault(elementId, fault, actor.Name), actor, $"Inject {fault} on {elementId}", cancellationToken);
    public Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetFault(elementId, GridEquipmentFault.None, actor.Name), actor, $"Clear fault {elementId}", cancellationToken);
    public Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetSpoof(measurementId, value, actor.Name), actor, $"Spoof {measurementId}", cancellationToken);
    public Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetProtection(protectionId, pickupValue, timeSetting, enabled, actor.Name), actor, $"Change protection {protectionId}", cancellationToken);
    public Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.OperateSwitch(switchId, closed, actor.Name, GridChangeCause.Unattributed), actor, $"Force switch {switchId}", cancellationToken);
    public Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetTap(transformerId, tap, actor.Name, GridChangeCause.Unattributed), actor, $"Force tap {transformerId}", cancellationToken);
    public Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.SetRtuOnline(rtuId, online, actor.Name), actor, $"RTU {rtuId} online={online}", cancellationToken);
    public Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken) =>
        Apply(w => w.RestoreTrainingDefaults(actor.Name), actor, "Restore training defaults", cancellationToken);

    private Task<GridCommandResult> Apply(Func<GridWorld, GridCommandResult> mutation, Actor actor, string description, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actor.Name) || !actor.HasRole(AccessRole.Administrator))
            return Task.FromResult(coordinator.Audit(GridCommandResult.Reject(GridCommandRejection.NotAuthorized, "Administrator role required."), actor, "training", description));
        return coordinator.ApplyAsync(mutation, actor, "training", true, cancellationToken, description);
    }
}
