using global::Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace UnitTests.Security.Grid.Support;

/// <summary>
/// Fake <see cref="IGridTraining"/>: records every call and applies it to the <see cref="SecurityFakeGridView"/>,
/// queueing the step events a real engine would raise (forced operations are Unattributed).
/// </summary>
public sealed class SecurityFakeGridTraining(SecurityFakeGridView view) : IGridTraining
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public Task<GridCommandResult> InjectFaultAsync(string elementId, GridEquipmentFault fault, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"InjectFault {elementId} {fault} by {actor.Name}");
        view.SetFault(elementId, fault);
        view.Emit(new GridStepEvent(GridStepEventKind.FaultInjected, elementId, $"Fault {fault} injected on {elementId}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok($"Fault {fault} injected on {elementId}."));
    }

    public Task<GridCommandResult> ClearFaultAsync(string elementId, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ClearFault {elementId} by {actor.Name}");
        view.SetFault(elementId, GridEquipmentFault.None);
        view.Emit(new GridStepEvent(GridStepEventKind.FaultCleared, elementId, $"Fault cleared on {elementId}", actor.Name, GridChangeCause.System));
        return Task.FromResult(GridCommandResult.Ok($"Fault cleared on {elementId}."));
    }

    public Task<GridCommandResult> SpoofMeasurementAsync(string measurementId, double? value, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"SpoofMeasurement {measurementId} = {value?.ToString() ?? "clear"} by {actor.Name}");
        view.SetSpoof(measurementId, value);
        if (value is not null)
            view.Emit(new GridStepEvent(GridStepEventKind.MeasurementSpoofed, measurementId, $"Measurement {measurementId} reported as {value}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok(value is null ? $"Spoof cleared on {measurementId}." : $"Measurement {measurementId} spoofed to {value}."));
    }

    public Task<GridCommandResult> ChangeProtectionAsync(string protectionId, double pickupValue, double timeSetting, bool enabled,
        Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ChangeProtection {protectionId} pickup={pickupValue} time={timeSetting} enabled={enabled} by {actor.Name}");
        if (enabled && pickupValue < 10_000)
        {
            view.ClearProtectionOverride(protectionId);
        }
        else
        {
            view.SetProtection(protectionId, pickupValue, timeSetting, enabled);
            view.Emit(new GridStepEvent(GridStepEventKind.ProtectionSettingChanged, protectionId,
                $"Protection {protectionId}: pickup={pickupValue}, time={timeSetting}, enabled={enabled}", actor.Name, GridChangeCause.Unattributed));
        }
        return Task.FromResult(GridCommandResult.Ok($"Protection {protectionId} changed."));
    }

    public Task<GridCommandResult> ForceSwitchAsync(string switchId, bool closed, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ForceSwitch {switchId} closed={closed} by {actor.Name}");
        var name = view.Definition.Switches.First(s => s.Id == switchId).Name;
        view.SetSwitchState(switchId, closed, GridChangeCause.Unattributed, actor.Name);
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, switchId,
            $"{name} {(closed ? "closed" : "opened")} directly (forced)", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok($"Switch {switchId} forced {(closed ? "closed" : "open")}."));
    }

    public Task<GridCommandResult> ForceTapAsync(string transformerId, int tap, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"ForceTap {transformerId} = {tap} by {actor.Name}");
        view.SetTap(transformerId, tap, automatic: false);
        view.Emit(new GridStepEvent(GridStepEventKind.TapChanged, transformerId, $"Tap of {transformerId} forced to {tap}", actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok($"Tap of {transformerId} forced to {tap}."));
    }

    public Task<GridCommandResult> SetRtuOnlineAsync(string rtuId, bool online, Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"SetRtuOnline {rtuId} = {online} by {actor.Name}");
        view.SetRtuOnline(rtuId, online);
        view.Emit(new GridStepEvent(GridStepEventKind.RtuOnlineChanged, rtuId, online ? $"RTU {rtuId} online" : $"RTU {rtuId} offline",
            actor.Name, GridChangeCause.Unattributed));
        return Task.FromResult(GridCommandResult.Ok($"RTU {rtuId} {(online ? "online" : "offline")}."));
    }

    public Task<GridCommandResult> RestoreTrainingDefaultsAsync(Actor actor, CancellationToken cancellationToken)
    {
        _calls.Add($"RestoreTrainingDefaults by {actor.Name}");
        view.RestoreDefaults();
        return Task.FromResult(GridCommandResult.Ok("Training defaults restored."));
    }
}
