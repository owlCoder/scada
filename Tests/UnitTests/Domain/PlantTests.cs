using Application;
using Domain;
using Infrastructure;
using Security;
using Xunit;

namespace UnitTests.Domain;

public sealed class PlantTests
{
    [Fact]
    public Task PumpIncreasesTankLevel()
    {
        var plant = new Plant();
        plant.SetPump(true);
        plant.Advance(10);
        Assert.Equal(55, plant.GetState().TankLevel, 5);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TankLevelNeverDropsBelowZero()
    {
        var plant = new Plant();
        for (var i = 0; i < 10; i++)
            plant.Advance(60);
        Assert.Equal(0, plant.GetState().TankLevel);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(61)]
    public Task InvalidTimeStepIsRejected(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Plant().Advance(seconds));
        return Task.CompletedTask;
    }

    [Fact]
    public Task CommandIsRecorded()
    {
        var store = new InMemoryEventStore();
        var service = new PlantService(new Plant(), store, new InMemoryTelemetryStore());
        service.SetPump(true, "operator");
        Assert.Single(store.GetRecent());
        return Task.CompletedTask;
    }

    [Fact]
    public Task ExcessiveCommandsAreRejected()
    {
        var monitor = new CommandMonitor(new InMemoryEventStore());
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("operator"));
        Assert.False(monitor.Check("operator"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task RejectedCommandsDoNotExtendRateLimitWindow()
    {
        var events = new InMemoryEventStore();
        var monitor = new CommandMonitor(events);
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("key-fingerprint"));

        Assert.False(monitor.Check("key-fingerprint"));
        Assert.False(monitor.Check("key-fingerprint"));
        Assert.Single(events.GetRecent(), item => item.Category == "SecurityAlert");
        return Task.CompletedTask;
    }

    [Fact]
    public Task BlankIdentityIsRejected()
    {
        var monitor = new CommandMonitor(new InMemoryEventStore());
        Assert.False(monitor.Check(""));
        return Task.CompletedTask;
    }

    [Fact]
    public Task WrongApiKeyIsRejected()
    {
        Assert.False(ApiKeyValidator.Matches("secret-key", "wrong-key"));
        Assert.True(ApiKeyValidator.Matches("secret-key", "secret-key"));
        return Task.CompletedTask;
    }
}
