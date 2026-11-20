using Domain;
using Xunit;

namespace UnitTests.Domain;

public sealed class PlantResultTests
{
    [Fact]
    public Task CommandsReturnTheAppliedState()
    {
        var plant = new Plant();
        var initial = plant.GetState();

        var running = plant.SetPump(true);
        Assert.True(running.PumpRunning);
        Assert.Equal(initial.Revision + 1, running.Revision);

        var unchanged = plant.SetPump(true);
        Assert.Equal(running.Revision, unchanged.Revision);

        var closed = plant.SetOutletValve(false);
        Assert.False(closed.OutletValveOpen);
        Assert.Equal(running.Revision + 1, closed.Revision);

        var advanced = plant.Advance(1);
        Assert.True(advanced.TankLevel > closed.TankLevel);
        Assert.Equal(closed.Revision + 1, advanced.Revision);
        return Task.CompletedTask;
    }
}
