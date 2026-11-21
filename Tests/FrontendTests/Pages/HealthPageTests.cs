using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class HealthPageTests
{
    [Fact]
    public async Task ViewerSeesStorageQueueSimulationDetectionAndSubscriberMetrics()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await host.Rig.StepAsync();
        var page = host.Context.Render<Health>();

        Assert.Contains("memory", page.Markup);
        Assert.Contains("Available", page.Markup);
        Assert.Contains("Persistence queue", page.Markup);
        Assert.Contains("Detection metrics", page.Markup);
        Assert.Contains("abnormal-tank-level", page.Markup);
        Assert.Contains("Realtime subscribers", page.Markup);
        Assert.Contains("Running", page.Markup);
        Assert.Equal("Check storage", Assert.Single(page.FindAll("button")).TextContent);

        host.Rig.Coordinator.SetPaused(true, new Application.Identity.Actor("tester", Application.Identity.AccessRole.Operator));
        page.WaitForAssertion(() => Assert.Contains("Paused", page.Markup));
    }
}
