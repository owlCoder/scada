using Application.Identity;
using Application.Realtime;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace FrontendTests.Shared;

public sealed class RealtimeUpdateTests
{
    [Fact]
    public async Task LifetimeTokenRemainsReadableByLateHandlersAfterDisposal()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<RealtimeProbeComponent>();
        var component = page.Instance;
        await host.Context.DisposeComponentsAsync();
        Assert.True(component.LifetimeToken.IsCancellationRequested);
    }

    [Fact]
    public async Task StoreFailuresShowABannerAndRefreshRecoversAfterInitializationActionsAndLiveUpdates()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var store = new FailingEventStore();
        host.Context.Services.AddSingleton<Application.Events.IEventStore>(store);
        var page = host.Context.Render<Events>();
        Assert.Contains("could not be refreshed", page.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("private database failure", page.Markup);
        store.Fail = false;
        host.Broadcaster.Publish(RealtimeTopics.EventsPersisted, 1);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=alert]")));
        store.Fail = true;
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[role=alert]")));
        var calls = store.Calls;
        host.Broadcaster.Publish(RealtimeTopics.EventsPersisted, 1);
        page.WaitForAssertion(() => Assert.True(store.Calls > calls));
        store.Fail = false;
        host.Broadcaster.Publish(RealtimeTopics.EventsPersisted, 1);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=alert]")));
    }

    [Fact]
    public async Task PagesSubscribeWhileShownAndRefreshOnPlantMessages()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        var page = host.Context.Render<Pumps>();
        Assert.Equal(1, host.Broadcaster.SubscriberCount);

        host.Rig.Engine.Mutate(w => w.Plant.SetMode("P-101", Domain.Process.Devices.ControlMode.Manual,
            Domain.Process.Commands.CommandOrigin.Operator("instructor")));
        host.Broadcaster.Publish(RealtimeTopics.Plant, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()));

        page.WaitForAssertion(() => Assert.Contains("Manual", page.FindAll("tbody tr")[0].TextContent), TimeSpan.FromSeconds(3));
        await host.Context.DisposeComponentsAsync();
        Assert.Equal(0, host.Broadcaster.SubscriberCount);
    }
}
