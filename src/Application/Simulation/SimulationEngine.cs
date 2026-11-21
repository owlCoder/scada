using Domain.Control;
using Domain.Network;
using Domain.Process;
using Domain.Process.Commands;
using Domain.Process.Snapshots;

namespace Application.Simulation;

/// <summary>
/// Owns the simulated world and serialises every access to it. Each step runs controller scans first,
/// then supervisory polling, then the plant physics, so behaviour is fully deterministic for a given seed.
/// </summary>
public sealed class SimulationEngine
{
    private readonly Lock _sync = new();
    private readonly SimulationTopology _topology;
    private readonly SimulationOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, int> _supervisoryFailures = new(StringComparer.Ordinal);
    private SimulationWorld _world;
    private PlantFieldBus _fieldBus;

    public SimulationEngine(SimulationTopology topology, SimulationOptions options, TimeProvider time)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        var errors = topology.Validate().Concat(options.Validate()).ToArray();
        if (errors.Length > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(topology));

        (_world, _fieldBus) = CreateWorld(new Plant(topology.Plant));
        if (options.StartPaused)
            _world.Plant.Clock.Pause();
    }

    public SimulationTopology Topology => _topology;
    public SimulationOptions Options => _options;
    public TimeSpan StepInterval => TimeSpan.FromSeconds(_options.StepSeconds);

    public SimulationStatus Status
    {
        get
        {
            lock (_sync)
            {
                var clock = _world.Plant.Clock;
                return new SimulationStatus(clock.IsPaused, clock.SimulationTime, clock.StepCount,
                    _options.StepSeconds, _options.TimeScale, _options.RandomSeed, _world.Plant.Revision);
            }
        }
    }

    /// <summary>Advances one step. When paused the step only runs if <paramref name="force"/> is true (manual stepping).</summary>
    public SimulationStepReport? Step(bool force = false)
    {
        lock (_sync)
        {
            var plant = _world.Plant;
            if (plant.Clock.IsPaused && !force)
                return null;

            var step = StepInterval;
            var now = _time.GetUtcNow();
            var scans = new List<ControllerScanResult>();
            foreach (var controller in _world.Controllers)
            {
                var due = controller.AccumulateScans(step);
                for (var i = 0; i < due; i++)
                {
                    var scan = controller.Scan(_fieldBus, plant.Clock.SimulationTime);
                    scans.Add(scan);
                    if (scan.EnteredSafeState)
                        plant.ApplyFailSafe(scan.FailSafeDevices);
                }
            }

            var supervisory = PollSupervisory();
            var result = plant.Step(step, now);
            return new SimulationStepReport(now, result, scans, _fieldBus.Drain(), supervisory,
                _world.Controllers.Select(c => c.Snapshot()).ToArray(), _world.Network.Snapshot());
        }
    }

    public bool Pause()
    {
        lock (_sync)
            return _world.Plant.Clock.Pause();
    }

    public bool Resume()
    {
        lock (_sync)
            return _world.Plant.Clock.Resume();
    }

    /// <summary>Recreates the plant, controllers and network from configuration.</summary>
    public PlantSnapshot Reset()
    {
        lock (_sync)
        {
            var paused = _world.Plant.Clock.IsPaused;
            (_world, _fieldBus) = CreateWorld(new Plant(_topology.Plant));
            if (paused)
                _world.Plant.Clock.Pause();
            return _world.Plant.CaptureSnapshot();
        }
    }

    public SimulationStateSnapshot CaptureState()
    {
        lock (_sync)
        {
            return new SimulationStateSnapshot(_time.GetUtcNow(), _world.Plant.CaptureSnapshot(),
                _world.Controllers.Select(c => new ControllerStateRecord(c.Id, c.State,
                    c.Definition.PumpLoops.SelectMany(l => new[] { l.StartLevelRegister, l.StopLevelRegister })
                        .Concat(c.Definition.ValveLoops.SelectMany(l => new[] { l.OpenBelowRegister, l.CloseAboveRegister }))
                        .Select(a => new SetpointValue(a, c.Registers.Read(RegisterArea.Holding, a)))
                        .ToArray())).ToArray(),
                _world.Plant.Clock.IsPaused);
        }
    }

    public bool Restore(SimulationStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            if (!string.Equals(state.Plant.PlantName, _topology.Plant.Name, StringComparison.Ordinal))
                return false;

            var (world, bus) = CreateWorld(Plant.Restore(_topology.Plant, state.Plant));
            foreach (var record in state.Controllers)
                world.FindController(record.ControllerId)?.Restore(record.State,
                    record.Setpoints.Select(s => (s.Address, s.Value)));
            if (state.IsPaused)
                world.Plant.Clock.Pause();
            _world = world;
            _fieldBus = bus;
            return true;
        }
    }

    /// <summary>Runs a read-only query against the world under the engine lock.</summary>
    public T Read<T>(Func<SimulationWorld, T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_sync)
            return query(_world);
    }

    /// <summary>Applies a mutation to the world under the engine lock.</summary>
    public T Mutate<T>(Func<SimulationWorld, T> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (_sync)
            return mutation(_world);
    }

    /// <summary>
    /// Checks that a supervisory command can reach the device: the owning controller must be reachable over its
    /// supervisory link, running, and able to reach its field I/O.
    /// </summary>
    public CommandResult CheckCommandPath(string deviceId)
    {
        lock (_sync)
        {
            if (_world.OwnerOf(deviceId) is not { } controller)
                return _world.Plant.ContainsDevice(deviceId)
                    ? CommandResult.Success(deviceId, "Device has no controller")
                    : CommandResult.Rejected(deviceId, CommandRejectionReason.UnknownDevice, $"Unknown device {deviceId}.");

            if (controller.State != ControllerState.Running)
                return CommandResult.Rejected(deviceId, CommandRejectionReason.CommunicationUnavailable,
                    $"{controller.Id} is in {controller.State} state.");

            foreach (var link in new[] { controller.Definition.SupervisoryLinkId, controller.Definition.FieldLinkId })
            {
                var transmission = _world.Network.Transmit(link);
                if (!transmission.Delivered)
                    return CommandResult.Rejected(deviceId, CommandRejectionReason.CommunicationUnavailable,
                        $"Command to {deviceId} via {controller.Id} failed on {link}: {transmission.Outcome}.");
            }

            return CommandResult.Success(deviceId, $"Path to {deviceId} via {controller.Id} available");
        }
    }

    private IReadOnlyList<SupervisoryStatusChange> PollSupervisory()
    {
        var changes = new List<SupervisoryStatusChange>();
        foreach (var controller in _world.Controllers)
        {
            var transmission = _world.Network.Transmit(controller.Definition.SupervisoryLinkId);
            var failures = transmission.Delivered ? 0 : _supervisoryFailures.GetValueOrDefault(controller.Id) + 1;
            _supervisoryFailures[controller.Id] = failures;
            var state = failures == 0 ? CommunicationState.Online
                : failures >= _options.SupervisoryTimeoutPolls ? CommunicationState.Offline
                : CommunicationState.Degraded;
            var previous = controller.SupervisoryCommunication;
            if (controller.SetSupervisoryCommunication(state))
                changes.Add(new SupervisoryStatusChange(controller.Id, previous, state));
        }

        return changes;
    }

    private (SimulationWorld World, PlantFieldBus Bus) CreateWorld(Plant plant)
    {
        var network = new VirtualNetwork(_topology.Network, new SeededRandomSource(_options.RandomSeed));
        var controllers = _topology.Controllers.Select(ControllerFactory.Create).ToArray();
        _supervisoryFailures.Clear();
        return (new SimulationWorld(plant, network, controllers), new PlantFieldBus(plant, network));
    }
}
