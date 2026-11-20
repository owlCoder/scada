using Application;
using Domain;
using Infrastructure;
using Xunit;

namespace UnitTests;

public sealed class ProcessTests
{
    [Fact]
    public void ClosedValveStopsOutflow()
    {
        var plant = new Plant();
        plant.SetOutletValve(false);
        plant.Advance(10);

        var state = plant.GetState();
        Assert.Equal(50, state.TankLevel);
        Assert.Equal(0, state.OutflowRate);
        Assert.False(state.OutletValveOpen);
    }

    [Fact]
    public void FullTankStopsPumpAutomatically()
    {
        var plant = new Plant();
        plant.SetOutletValve(false);
        plant.SetPump(true);
        plant.Advance(60);
        plant.Advance(60);

        var state = plant.GetState();
        Assert.Equal(100, state.TankLevel);
        Assert.False(state.PumpRunning);
    }

    [Fact]
    public void FullTankRejectsPumpStart()
    {
        var plant = new Plant();
        plant.SetOutletValve(false);
        plant.SetPump(true);
        plant.Advance(60);
        plant.Advance(60);

        Assert.Throws<InvalidOperationException>(() => plant.SetPump(true));
    }

    [Fact]
    public void SafetyInterlockIsRecorded()
    {
        var events = new InMemoryEventStore();
        var service = new PlantService(new Plant(), events, new InMemoryTelemetryStore());
        service.SetOutletValve(false, "operator");
        service.SetPump(true, "operator");
        service.Advance(60);
        service.Advance(60);

        Assert.Contains(events.GetRecent(), e => e.Category == "SafetyInterlock");
    }

    [Fact]
    public void ValveCommandIsAudited()
    {
        var events = new InMemoryEventStore();
        var service = new PlantService(new Plant(), events, new InMemoryTelemetryStore());
        service.SetOutletValve(false, "operator");

        Assert.Contains(events.GetRecent(), e => e.Message == "Излазни вентил затворен");
    }
}
