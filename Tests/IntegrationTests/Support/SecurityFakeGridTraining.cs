using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace IntegrationTests.Support;

/// <summary>Minimal <see cref="IGridTraining"/> for API-level grid security tests: records calls, queues events, accepts everything.</summary>
public sealed class SecurityFakeGridTraining(SecurityFakeGridView view) : IGridTraining
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"InjectFault {elementId} {fault}");
        view.Emit(new GridStepEvent(GridStepEventKind.FaultInjected, elementId, $"Fault {fault} on {elementId}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok("Fault injected."));
    }

    public Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ClearFault {elementId}");
        return Task.FromResult(GridCommandResult.Ok("Fault cleared."));
    }

    public Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"SpoofMeasurement {measurementId} = {value?.ToString() ?? "clear"}");
        if (value is not null)
            view.Emit(new GridStepEvent(GridStepEventKind.MeasurementSpoofed, measurementId, $"Spoofed to {value}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok("Spoof set."));
    }

    public Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled,
        Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ChangeProtection {protectionId} enabled={enabled}");
        view.Emit(new GridStepEvent(GridStepEventKind.ProtectionSettingChanged, protectionId,
            $"pickup={pickupValue} time={timeSetting} enabled={enabled}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok("Protection changed."));
    }

    public Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ForceSwitch {switchId} closed={closed}");
        view.SetBreakerClosed(closed);
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, switchId, $"Forced {(closed ? "close" : "open")}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok("Switch forced."));
    }

    public Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ForceTap {transformerId} = {tap}");
        return Task.FromResult(GridCommandResult.Ok("Tap forced."));
    }

    public Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"SetRtuOnline {rtuId} = {online}");
        view.Emit(new GridStepEvent(GridStepEventKind.RtuOnlineChanged, rtuId, online ? $"RTU {rtuId} online" : $"RTU {rtuId} offline", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok("RTU state set."));
    }

    public Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add("RestoreTrainingDefaults");
        return Task.FromResult(GridCommandResult.Ok("Training defaults restored."));
    }
}
