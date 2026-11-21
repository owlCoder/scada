using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Application.Realtime;

namespace FrontendTests.Pages;

public sealed class OverviewPageTests
{
    [Fact]
    public async Task SlowRefreshesAreAwaitedAndCancelledWhenThePageIsDisposed()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var store = new BlockingIncidentStore();
        host.Context.Services.AddSingleton(new Application.Incidents.IncidentService(store, host.Rig.Events, host.Broadcaster, host.Rig.Time));
        var page = host.Context.Render<Overview>();
        Assert.Equal(1, store.Calls);
        host.Broadcaster.Publish(RealtimeTopics.Incidents, 1);
        await store.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var i = 0; i < 10; i++)
            host.Broadcaster.Publish(RealtimeTopics.Incidents, i);
        Assert.Equal(2, store.Calls);
        Assert.True(store.LastToken.CanBeCanceled);
        await host.Context.DisposeComponentsAsync();
        Assert.True(store.LastToken.IsCancellationRequested);
        Assert.Equal(0, host.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task PauseAndResumeRequireConfirmationAndCancelLeavesTheSimulationRunning()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var page = host.Context.Render<Overview>();
        page.Find("button").Click();
        Assert.False(host.Rig.Engine.Status.IsPaused);
        page.FindAll(".dialog button").Single(b => b.TextContent == "Cancel").Click();
        Assert.False(host.Rig.Engine.Status.IsPaused);
        page.Find("button").Click();
        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.True(host.Rig.Engine.Status.IsPaused));
        page.Find("button").Click();
        Assert.True(host.Rig.Engine.Status.IsPaused);
        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.False(host.Rig.Engine.Status.IsPaused));
    }
}
