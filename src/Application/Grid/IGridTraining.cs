using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace Application.Grid;

/// <summary>
/// Instructor and scenario manipulation of the simulated network (Administrator role, audited). Nothing here is
/// available to operators; it models faults and attacker actions inside the training environment only.
/// </summary>
public interface IGridTraining
{
    Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled,
        Actor actor, CancellationToken cancellationToken);

    /// <summary>Operates a switch directly, bypassing select-before-operate (what a compromised RTU or protocol client does).</summary>
    Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken);

    Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken);
    Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken);

    /// <summary>Clears faults, spoofing and protection changes and restores RTUs; switch and tap states are left as they are.</summary>
    Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken);
}
