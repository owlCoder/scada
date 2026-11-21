using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Application.Realtime;

namespace FrontendTests.Pages;

public sealed class AlarmsPageTests
{
    [Fact]
    public async Task SlowRefreshesAreAwaitedAndCancelledWhenThePageIsDisposed()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var store = new BlockingAlarmHistoryStore();
        host.Context.Services.AddSingleton(new Application.Alarms.AlarmService(host.Rig.Engine, host.Rig.Events, host.Rig.Queue,
            host.Broadcaster, store, host.Rig.Time));
        var page = host.Context.Render<Alarms>();
        Assert.Equal(1, store.Calls);
        host.Broadcaster.Publish(RealtimeTopics.Alarms, 1);
        await store.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var i = 0; i < 10; i++)
            host.Broadcaster.Publish(RealtimeTopics.Alarms, i);
        Assert.Equal(2, store.Calls);
        Assert.True(store.LastToken.CanBeCanceled);
        await host.Context.DisposeComponentsAsync();
        Assert.True(store.LastToken.IsCancellationRequested);
        Assert.Equal(0, host.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task AlarmAcknowledgementRequiresConfirmationAndCanBeCancelled()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        host.Rig.Training.SetTankLevel("T-101", 99);
        await host.Rig.StepAsync();
        var page = host.Context.Render<Alarms>();
        var alarm = host.Rig.Alarms.GetCurrent().First(a => !a.IsAcknowledged);
        page.FindAll("button").First(b => b.TextContent == "Acknowledge").Click();
        Assert.False(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged);
        page.FindAll(".dialog button").Single(b => b.TextContent == "Cancel").Click();
        Assert.False(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged);
        page.FindAll("button").First(b => b.TextContent == "Acknowledge").Click();
        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.Contains(host.Rig.Alarms.GetCurrent(), a => a.IsAcknowledged && a.AcknowledgedBy == "eva"));
    }
}
