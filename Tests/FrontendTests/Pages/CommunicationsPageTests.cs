using Application.Identity;
using Bunit;
using Domain.Network;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class CommunicationsPageTests
{
    [Fact]
    public async Task OperatorSeesLinksAndDiagnosticsWithoutControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var page = host.Context.Render<Communications>();

        Assert.Contains("L-SCADA-PLC1", page.Markup);
        Assert.Contains("Latency", page.Markup);
        Assert.Contains("Packet loss", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task DisconnectRunsOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        var page = host.Context.Render<Communications>();
        var row = page.FindAll("tbody tr").Single(row => row.QuerySelector("strong")?.TextContent == "L-SCADA-PLC1");

        row.QuerySelector("button")!.Click();
        Assert.Contains("Disconnect L-SCADA-PLC1", page.Find(".dialog").TextContent);
        Assert.Equal(LinkState.Up, host.Rig.Engine.Read(world => world.Network.FindLink("L-SCADA-PLC1")!.State));

        page.Find(".dialog .btn-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Link disconnected", page.Find(".banner-ok").TextContent));
        Assert.Equal(LinkState.Down, host.Rig.Engine.Read(world => world.Network.FindLink("L-SCADA-PLC1")!.State));
    }
}
