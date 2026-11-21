using Application.Identity;
using Application.Realtime;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Shared;

public sealed class RealtimeUpdateTests
{
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
