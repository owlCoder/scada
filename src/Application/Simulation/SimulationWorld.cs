using Domain.Control;
using Domain.Network;
using Domain.Process;

namespace Application.Simulation;

/// <summary>Mutable simulation state. Only accessed inside <see cref="SimulationEngine"/>'s lock.</summary>
public sealed class SimulationWorld(Plant plant, VirtualNetwork network, IReadOnlyList<VirtualController> controllers)
{
    public Plant Plant { get; } = plant;
    public VirtualNetwork Network { get; } = network;
    public IReadOnlyList<VirtualController> Controllers { get; } = controllers;

    public VirtualController? FindController(string id) => Controllers.FirstOrDefault(c => c.Id == id);

    public VirtualController? OwnerOf(string deviceId) =>
        Controllers.FirstOrDefault(c => c.Definition.ActuatorIds.Contains(deviceId, StringComparer.Ordinal));
}
