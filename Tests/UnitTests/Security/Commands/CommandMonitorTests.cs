using Application.Events;
using Application.Persistence;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Time.Testing;
using Security.Commands;
using Xunit;

namespace UnitTests.Security.Commands;

public sealed class CommandMonitorTests
{
    private static (CommandMonitor Monitor, RecentEventBuffer Events, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var buffer = new RecentEventBuffer();
        var publisher = new EventPublisher(buffer, new ChannelPersistenceQueue(new PersistenceQueueOptions(), time), new NullRealtimePublisher());
        return (new CommandMonitor(publisher, time, new CommandRateOptions()), buffer, time);
    }

    [Fact]
    public Task AllowsTenCommandsAndRejectsTheEleventh()
    {
        var (monitor, events, _) = Create();

        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.Single(events.GetRecent(), item => item.Category == EventCategories.SecurityAlert);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RepeatedRejectionsProduceOnlyOneAlertPerWindow()
    {
        var (monitor, events, _) = Create();
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));
        for (var i = 0; i < 50; i++)
            Assert.False(monitor.Check("credential-a"));

        Assert.Single(events.GetRecent(), item => item.Category == EventCategories.SecurityAlert);
        return Task.CompletedTask;
    }

    [Fact]
    public Task CredentialsHaveIndependentLimits()
    {
        var (monitor, _, _) = Create();
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("credential-a"));

        Assert.False(monitor.Check("credential-a"));
        Assert.True(monitor.Check("credential-b"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task EmptyIdentityIsRejectedWithoutAnEvent()
    {
        var (monitor, events, _) = Create();
        Assert.False(monitor.Check(""));
        Assert.Empty(events.GetRecent());
        return Task.CompletedTask;
    }

    [Fact]
    public Task RejectedCommandsDoNotExtendTheWindow()
    {
        var (monitor, _, time) = Create();
        for (var i = 0; i < 10; i++)
            Assert.True(monitor.Check("key"));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(monitor.Check("key"));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(monitor.Check("key"));
        return Task.CompletedTask;
    }
}
