using Application.Events;
using Application.Simulation;
using Application.Telemetry;
using Domain.Control;
using Domain.Network;
using Domain.Process.Commands;
using Domain.Process.Snapshots;

namespace Application.Training;

public sealed class TrainingEnvironment(
    SimulationEngine engine,
    TelemetryHistorian historian,
    IEventPublisher events,
    TimeProvider time) : ITrainingEnvironment
{
    public PlantSnapshot Snapshot() => engine.Read(world => world.Plant.CaptureSnapshot());

    public CommandResult SpoofSensor(string sensorId, double? value) =>
        engine.Mutate(world => world.Plant.SpoofSensor(sensorId, value));

    public CommandResult InjectFault(FaultInjection fault) => engine.Mutate(world => world.Plant.InjectFault(fault));

    public CommandResult ClearFault(string deviceId) => engine.Mutate(world => world.Plant.ClearFault(deviceId));

    public CommandResult ForceActuator(string deviceId, double value, string actor) => engine.Mutate(world =>
    {
        var origin = CommandOrigin.Unattributed(actor);
        return world.Plant.FindPump(deviceId) is not null
            ? world.Plant.CommandPump(deviceId, value >= 0.5, origin)
            : world.Plant.CommandValve(deviceId, value, origin);
    });

    public CommandResult SetInterlockBypass(bool bypassed, string actor) =>
        engine.Mutate(world => world.Plant.SetInterlockBypass(bypassed, actor));

    public CommandResult SetTankLevel(string tankId, double levelPercent) =>
        engine.Mutate(world => world.Plant.SetTankLevel(tankId, levelPercent));

    public bool SetLinkConnected(string linkId, bool connected) => engine.Mutate(world =>
        world.Network.FindLink(linkId) is { } link && (connected ? link.Reconnect() : link.Disconnect()));

    public bool SetLinkImpairment(string linkId, double latencyMilliseconds, double packetLossPercent) => engine.Mutate(world =>
        world.Network.FindLink(linkId)?.SetImpairment(latencyMilliseconds, packetLossPercent) ?? false);

    public bool ForceRegister(string controllerId, RegisterArea area, ushort address, ushort value) => engine.Mutate(world =>
        world.FindController(controllerId)?.Registers.Force(area, address, value) ?? false);

    public int ReleaseRegisterForces(string controllerId) => engine.Mutate(world =>
        world.FindController(controllerId)?.Registers.ReleaseAll() ?? 0);

    public bool SetControllerRunning(string controllerId, bool running) => engine.Mutate(world =>
        world.FindController(controllerId) is { } controller && (running ? controller.Start() : controller.Stop()));

    public bool ResetController(string controllerId) => engine.Mutate(world =>
        world.FindController(controllerId)?.Reset() ?? false);

    public BlockedConnectionAttempt? AttemptConnection(string sourceNode, string sourceSegmentId, string targetNode, string targetSegmentId)
    {
        var blocked = engine.Mutate(world => world.Network.EvaluateConnection(sourceNode, sourceSegmentId, targetNode, targetSegmentId));
        if (blocked is not null)
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Network, EventSeverity.Medium, "zone-firewall",
                $"Blocked connection {blocked.SourceNode} ({blocked.SourceZone}) -> {blocked.TargetNode} ({blocked.TargetZone}): {blocked.Reason}",
                blocked.SourceNode, blocked.TargetNode));
        return blocked;
    }

    public bool TamperHistorian(string tagId, double value) => historian.Tamper(tagId, value);

    public int RestoreDefaults() => engine.Mutate(world =>
    {
        var restored = 0;
        foreach (var sensor in world.Plant.Sensors.Where(s => s.IsSpoofed))
            restored += world.Plant.SpoofSensor(sensor.Id, null).Accepted ? 1 : 0;
        foreach (var id in world.Plant.Pumps.Where(p => p.Fault != Domain.Process.Devices.PumpFault.None).Select(p => p.Id)
            .Concat(world.Plant.Valves.Where(v => v.Fault != Domain.Process.Devices.ValveFault.None).Select(v => v.Id))
            .Concat(world.Plant.Sensors.Where(s => s.Fault != Domain.Process.Devices.SensorFault.None).Select(s => s.Id))
            .ToArray())
            restored += world.Plant.ClearFault(id).Accepted ? 1 : 0;
        if (world.Plant.InterlocksBypassed)
            restored += world.Plant.SetInterlockBypass(false, "scenario-engine").Accepted ? 1 : 0;
        restored += world.Network.RestoreDefaults();
        foreach (var controller in world.Controllers)
        {
            restored += controller.RestoreDefaults();
            if (controller.State == ControllerState.Stopped)
                restored += controller.Start() ? 1 : 0;
        }

        return restored;
    });
}
