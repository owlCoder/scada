using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace Api.Hosting;

/// <summary>Fallback <see cref="IGridTraining"/> for hosts without a grid simulation; see <see cref="UnavailableGridView"/>.</summary>
public sealed class UnavailableGridTraining : IGridTraining
{
    private static GridCommandResult Unavailable() =>
        GridCommandResult.Reject(GridCommandRejection.CommunicationLoss, "Grid mode is not available in this host.");

    public Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled,
        Actor actor, CancellationToken cancellationToken) => Task.FromResult(Unavailable());

    public Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());
}
