using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace FrontendTests.Support;

public sealed class HmiFakeGridTraining : IGridTraining
{
    public List<(string Action, string Target, Actor Actor)> Calls { get; } = [];
    private Task<GridCommandResult> Record(string action, string target, Actor actor)
    {
        Calls.Add((action, target, actor));
        return Task.FromResult(GridCommandResult.Ok("Training action accepted."));
    }
    public Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken) => Record($"Inject {fault}", elementId, actor);
    public Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken) => Record("Clear fault", elementId, actor);
    public Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken) => Record($"Spoof {value}", measurementId, actor);
    public Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled, Actor actor, CancellationToken cancellationToken) => Record($"Protection {pickupValue}/{timeSetting}/{enabled}", protectionId, actor);
    public Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken) => Record($"Force switch {closed}", switchId, actor);
    public Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken) => Record($"Force tap {tap}", transformerId, actor);
    public Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken) => Record($"RTU {online}", rtuId, actor);
    public Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken) => Record("Restore defaults", "grid", actor);
}
