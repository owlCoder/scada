using Domain.Control;
using Domain.Network;
using Domain.Process.Configuration;

namespace Application.Simulation;

public sealed record SimulationTopology(
    PlantDefinition Plant,
    NetworkDefinition Network,
    IReadOnlyList<ControllerDefinition> Controllers)
{
    public static SimulationTopology Default() => new(
        DefaultPlantDefinition.Create(), DefaultNetworkDefinition.Create(), DefaultControllerDefinitions.Create());

    public IReadOnlyList<string> Validate()
    {
        var errors = PlantDefinitionValidator.Validate(Plant).ToList();
        var links = Network.Links.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var devices = Plant.Pumps.Select(p => p.Id).Concat(Plant.Valves.Select(v => v.Id))
            .Concat(Plant.Sensors.Select(s => s.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (var controller in Controllers)
        {
            if (!links.Contains(controller.FieldLinkId) || !links.Contains(controller.SupervisoryLinkId))
                errors.Add($"Controller '{controller.Id}' references an unknown link.");
            if (controller.Inputs.Concat(controller.Outputs).Any(p => !devices.Contains(p.DeviceId)))
                errors.Add($"Controller '{controller.Id}' maps an unknown device.");
        }

        var owners = Controllers.SelectMany(c => c.ActuatorIds.Select(id => (id, c.Id)))
            .GroupBy(x => x.id).Where(g => g.Count() > 1);
        errors.AddRange(owners.Select(g => $"Actuator '{g.Key}' is owned by more than one controller."));
        if (Controllers.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Controllers.Count)
            errors.Add("Controller identifiers must be unique.");
        return errors;
    }
}
