using Infrastructure;
using Security;
using Xunit;

namespace UnitTests;

public sealed class CommandMonitorTests
{
    [Fact]
    public void AllowsTenCommandsAndRejectsTheEleventh()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.Single(store.GetRecent(), item => item.Category == "SecurityAlert");
    }

    [Fact]
    public void RepeatedRejectionsProduceOnlyOneAlertPerWindow()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        for (var i = 0; i < 50; i++)
            Assert.False(monitor.Check("credential-a"));

        Assert.Single(store.GetRecent(), item => item.Category == "SecurityAlert");
    }

    [Fact]
    public void CredentialsHaveIndependentLimits()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.True(monitor.Check("credential-b"));
    }

    [Fact]
    public void EmptyIdentityIsRejectedWithoutAnEvent()
    {
        var store = new InMemoryEventStore();
        var monitor = new CommandMonitor(store);

        Assert.False(monitor.Check(""));
        Assert.Empty(store.GetRecent());
    }
}
