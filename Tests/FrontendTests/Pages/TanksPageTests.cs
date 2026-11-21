using Application.Identity;
using Application.Realtime;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class TanksPageTests
{
    [Fact]
    public async Task ViewerSeesLimitsCapacityAndOverflowWithNoControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Tanks>();

        Assert.Equal(3, page.FindAll("[role=meter]").Count);
        Assert.Contains("T-101", page.Find("tbody").TextContent);
        Assert.Contains("m³", page.Find("tbody").TextContent);
        Assert.Contains("Clear", page.Find("tbody").TextContent);
        Assert.Equal(["LL", "L", "H", "HH"], page.FindAll("thead th").Skip(3).Take(4).Select(header => header.TextContent));
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task LevelRefreshesFromReportedInstrumentReading()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var page = host.Context.Render<Tanks>();

        Assert.True(host.Rig.Training.SpoofSensor("LT-101", 97).Accepted);
        host.Broadcaster.Publish(RealtimeTopics.Plant, host.Rig.Training.Snapshot());

        page.WaitForAssertion(() =>
        {
            Assert.Equal("97", page.Find("[role=meter]").GetAttribute("aria-valuenow"));
            Assert.NotNull(page.Find("tbody tr:first-child .level-fill-alarm"));
        });
    }
}
