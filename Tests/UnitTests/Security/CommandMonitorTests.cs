using Infrastructure;
using Security;
using Xunit;

namespace UnitTests.Security;

public sealed class CommandMonitorTests
{
    [Fact]
    public Task AllowsTenCommandsAndRejectsTheEleventh()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.Single(store.GetRecent(), item => item.Category == "SecurityAlert");
        return Task.CompletedTask;
    }

    [Fact]
    public Task RepeatedRejectionsProduceOnlyOneAlertPerWindow()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        for (var i = 0; i < 50; i++)
            Assert.False(monitor.Check("credential-a"));

        Assert.Single(store.GetRecent(), item => item.Category == "SecurityAlert");
        return Task.CompletedTask;
    }

    [Fact]
    public Task CredentialsHaveIndependentLimits()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.True(monitor.Check("credential-b"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task EmptyIdentityIsRejectedWithoutAnEvent()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        Assert.False(monitor.Check(""));
        Assert.Empty(store.GetRecent());
        return Task.CompletedTask;
    }
}
