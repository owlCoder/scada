using Application.Simulation;
using Simulation.Configuration;
using Xunit;

namespace UnitTests.Simulation.Configuration;

public sealed class PlantConfigurationLoaderTests
{
    [Fact]
    public Task DefaultTopologyRoundTripsThroughJson()
    {
        var json = PlantConfigurationLoader.Serialize(SimulationTopology.Default());

        var parsed = PlantConfigurationLoader.Parse(json);

        Assert.Equal(SimulationTopology.Default().Plant.Tanks.Select(t => t.Id), parsed.Plant.Tanks.Select(t => t.Id));
        Assert.Equal(3, parsed.Controllers.Count);
        Assert.Empty(parsed.Validate());
        Assert.Equal(json, PlantConfigurationLoader.Serialize(parsed));
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileIsLoadedWhenConfiguredAndDefaultOtherwise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"topology-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, PlantConfigurationLoader.Serialize(SimulationTopology.Default()));
        try
        {
            Assert.Equal("Water Treatment Plant", PlantConfigurationLoader.Load(path).Plant.Name);
            Assert.Equal("Water Treatment Plant", PlantConfigurationLoader.Load(null).Plant.Name);
            Assert.Throws<FileNotFoundException>(() => PlantConfigurationLoader.Load(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidJsonOrTopologyIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PlantConfigurationLoader.Parse("{ not json"));
        Assert.Throws<InvalidOperationException>(() => PlantConfigurationLoader.Parse("{}"));

        var json = PlantConfigurationLoader.Serialize(SimulationTopology.Default()).Replace("\"fieldLinkId\": \"L-PLC1-IO\"", "\"fieldLinkId\": \"L-MISSING\"");
        var error = Assert.Throws<InvalidOperationException>(() => PlantConfigurationLoader.Parse(json));
        Assert.Contains("unknown link", error.Message);
        return Task.CompletedTask;
    }
}
