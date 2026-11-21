using Protocols.OpcUa.Nodes;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols;

public sealed class PlantNodeValuesTests
{
    [Fact]
    public async Task MapsAllDeviceTypesWithOnlyOperatorMeasurements()
    {
        var rig = new TestRig();
        Assert.True(rig.Training.SpoofSensor("LT-101", 25).Accepted);
        Assert.True(rig.Training.SpoofSensor("FT-102", 1.75).Accepted);
        await rig.StepAsync();

        var snapshot = rig.Training.Snapshot();
        var controllers = rig.Engine.Read(world => world.Controllers.Select(controller => controller.Snapshot()).ToArray());
        var devices = PlantNodeValues.Map(snapshot, controllers).ToDictionary(device => (device.Folder, device.Id));

        Assert.Equal(snapshot.Tanks.Count + snapshot.Pumps.Count + snapshot.Valves.Count + snapshot.Sensors.Count + controllers.Length,
            devices.Count);
        var tank = devices[("Tanks", "T-101")].Values;
        Assert.Equal(25d, tank["Level"]);
        Assert.Equal(15d, tank["Volume"]);
        Assert.Equal(1.75d, devices[("Pumps", "P-101")].Values["Flow"]);
        Assert.Equal("Automatic", devices[("Valves", "V-301")].Values["Mode"]);
        Assert.Equal(controllers[0].State.ToString(), devices[("Controllers", controllers[0].Id)].Values["State"]);
        var sensor = devices[("Sensors", "LT-101")].Values;
        Assert.Equal(25d, sensor["Value"]);
        Assert.Equal(new[] { "Health", "Quality", "Unit", "Value" }, sensor.Keys.Order());
        Assert.NotEqual(25, snapshot.Sensor("LT-101")!.TrueValue);

        var uninstrumented = snapshot with { Sensors = snapshot.Sensors.Where(s => s.Id != "LT-101").ToArray() };
        var uninstrumentedTank = PlantNodeValues.Map(uninstrumented, controllers).Single(device => device.Id == "T-101");
        Assert.Equal(new[] { "Capacity" }, uninstrumentedTank.Values.Keys);
    }
}
