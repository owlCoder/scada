using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Simulation;

namespace Simulation.Configuration;

/// <summary>Loads a <see cref="SimulationTopology"/> from JSON and validates it before the engine is built.</summary>
public static class PlantConfigurationLoader
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static SimulationTopology Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return SimulationTopology.Default();
        if (!File.Exists(path))
            throw new FileNotFoundException($"Topology file '{path}' was not found.", path);

        return Parse(File.ReadAllText(path));
    }

    public static SimulationTopology Parse(string json)
    {
        SimulationTopology? topology;
        try
        {
            topology = JsonSerializer.Deserialize<SimulationTopology>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Topology JSON is invalid: {exception.Message}", exception);
        }

        if (topology?.Plant is null || topology.Network is null || topology.Controllers is null)
            throw new InvalidOperationException("Topology JSON must define plant, network and controllers.");

        var errors = topology.Validate();
        return errors.Count == 0
            ? topology
            : throw new InvalidOperationException($"Topology is invalid: {string.Join(" ", errors)}");
    }

    public static string Serialize(SimulationTopology topology) => JsonSerializer.Serialize(topology, SerializerOptions);
}
