using Application.Commands;
using Application.Identity;
using AngleSharp.Dom;
using Bunit;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PidContextMenuTests
{
    private static IElement Device(IRenderedComponent<Diagram> page, string ariaLabel) =>
        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == ariaLabel);

    [Fact]
    public async Task ViewerGetsOnlyTrendAndDetailsInTheContextMenu()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Device(page, plant.Pumps[0].Name).TriggerEvent("oncontextmenu", new MouseEventArgs { Button = 2 });

        var items = page.FindAll(".pid-menu .pid-menu-item");
        Assert.Equal(2, items.Count);
        Assert.Equal("Trend", items[0].TextContent.Trim());
        Assert.Equal("Details", items[1].TextContent.Trim());
    }

    [Fact]
    public async Task OperatorMenuStartRunsThePumpOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var actor = new Actor("eva", AccessRole.Operator);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.SetManual, null, CommandSource.Hmi), actor).Accepted);
        await host.Rig.StepAsync(2);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Device(page, plant.Pumps[0].Name).TriggerEvent("oncontextmenu", new MouseEventArgs { Button = 2 });

        var items = page.FindAll(".pid-menu .pid-menu-item");
        Assert.Equal(4, items.Count);
        Assert.Equal("Start", items[0].TextContent.Trim());
        Assert.Equal("Automatic", items[1].TextContent.Trim()); // menu offers the target mode

        items[0].Click();
        Assert.Empty(page.FindAll(".pid-menu")); // menu closed, confirm dialog open
        Assert.Contains("Start P-101?", page.Find(".dialog").TextContent);
        Assert.Equal(PumpState.Stopped, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.State);

        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".dialog")));
        await host.Rig.StepAsync(40);
        Assert.Equal(PumpState.Running, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.State);
    }

    [Fact]
    public async Task ValveMenuSetPositionOpensTheControlWindow()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Device(page, plant.Valves[0].Name).TriggerEvent("oncontextmenu", new MouseEventArgs { Button = 2 });
        var items = page.FindAll(".pid-menu .pid-menu-item");
        Assert.Contains("Set position…", items.Select(i => i.TextContent.Trim()).ToArray());

        items.Single(i => i.TextContent.Trim() == "Set position…").Click();
        Assert.Empty(page.FindAll(".pid-menu"));
        Assert.Contains(plant.Valves[0].Name, page.Find("aside.detail").TextContent);
        Assert.NotNull(page.Find("aside.detail input.pid-slider"));
    }

    [Fact]
    public async Task TrendMenuItemAddsTheDeviceTagToTheTrendDockTab()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await host.Rig.StepAsync(5);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Device(page, plant.Pumps[0].Name).TriggerEvent("oncontextmenu", new MouseEventArgs { Button = 2 });
        page.FindAll(".pid-menu .pid-menu-item").Single(i => i.TextContent.Trim() == "Trend").Click();

        Assert.Equal("true", page.FindAll(".pid-dock-tab")[3].GetAttribute("aria-selected"));
        Assert.Equal("FT-102", page.FindAll(".pid-trend-picker select")[0].GetAttribute("value"));
        Assert.NotEmpty(page.FindAll(".pid-trend-line"));
        Assert.Contains("FT-102", page.Find(".pid-trend-legend").TextContent);
    }

    [Fact]
    public async Task EscapeClosesTheContextMenu()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Device(page, plant.Tanks[0].Name).TriggerEvent("oncontextmenu", new MouseEventArgs { Button = 2 });
        Assert.NotEmpty(page.FindAll(".pid-menu"));

        page.Find(".pid-workspace").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(page.FindAll(".pid-menu"));
    }
}
