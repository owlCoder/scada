using Application;
using Domain;
using Infrastructure;
using Security;
using Xunit;

namespace UnitTests;

public sealed class PlantTests
{
    [Fact]
    public void PumpIncreasesTankLevel()
    {
        var plant = new Plant();
        plant.SetPump(true);
        plant.Advance(10);
        Assert.Equal(55, plant.GetState().TankLevel, 5);
    }

    [Fact]
    public void TankLevelNeverDropsBelowZero()
    {
        var plant = new Plant();
        for (var i = 0; i < 10; i++)
            plant.Advance(60);
        Assert.Equal(0, plant.GetState().TankLevel);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(61)]
    public void InvalidTimeStepIsRejected(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Plant().Advance(seconds));
    }

    [Fact]
    public void CommandIsRecorded()
    {
        var store = new InMemoryEventStore();
        var service = new PlantService(new Plant(), store);
        service.SetPump(true, "operator");
        Assert.Single(store.GetRecent());
    }

    [Fact]
    public void ExcessiveCommandsAreRejected()
    {
        var monitor = new CommandMonitor(new InMemoryEventStore());
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("operator"));
        Assert.False(monitor.Check("operator"));
    }

    [Fact]
    public void WrongApiKeyIsRejected()
    {
        Assert.False(ApiKeyValidator.Matches("secret-key", "wrong-key"));
        Assert.True(ApiKeyValidator.Matches("secret-key", "secret-key"));
    }
}
