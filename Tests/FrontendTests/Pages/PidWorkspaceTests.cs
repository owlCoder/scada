using Application.Commands;
using Application.Identity;
using Application.Realtime;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Hmi.Services;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PidWorkspaceTests
{
    private static (double X, double Y, double W, double H) ViewBox(IRenderedComponent<Diagram> page)
    {
        var parts = page.Find(".pid-canvas").GetAttribute("viewBox")!.Split(' ');
        return (double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool SetupPidModule(HmiTestHost host, PidPoint first, PidPoint second)
    {
        var module = host.Context.JSInterop.SetupModule("./_content/Hmi/js/pid.js");
        module.Setup<bool>("capture", _ => true).SetResult(true);
        module.Setup<bool>("release", _ => true).SetResult(true);
        module.Setup<PidPoint>("point", _ => true).SetResult(second);
        module.Setup<PidPoint>("point", invocation => Math.Abs(Convert.ToDouble(invocation.Arguments[1]) - first.X) < 20)
            .SetResult(first);
        return true;
    }

    [Fact]
    public async Task WorkspaceRendersTheWholePidWithInstrumentsControllersArrowsAndMinimap()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var actor = new Actor("eva", AccessRole.Operator);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.SetManual, null, CommandSource.Hmi), actor).Accepted);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.StartPump, null, CommandSource.Hmi), actor).Accepted);
        await host.Rig.StepAsync(10);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());

        var page = host.Context.Render<Diagram>();

        Assert.NotNull(page.Find(".pid-canvas"));
        Assert.Equal(plant.Pumps.Count + plant.Valves.Count(v => v.Role != Domain.Process.Devices.ValveRole.Discharge),
            page.FindAll(".pid-canvas .pid-pipe").Count);
        Assert.True(plant.Pumps.Single(p => p.Id == "P-101").FlowRate > 0.001);
        Assert.NotEmpty(page.FindAll(".pid-canvas .pid-pipe-flow"));
        Assert.NotEmpty(page.FindAll(".pid-canvas .pid-flow-arrow"));
        Assert.Equal(plant.Sensors.Count, page.FindAll(".pid-canvas g.pid-instrument").Count);
        Assert.Contains("LT-101", page.Find(".pid-canvas").TextContent);
        Assert.Equal(3, page.FindAll(".pid-canvas g.pid-controller").Count);
        Assert.Contains("field Online", page.Find(".pid-canvas").TextContent);
        Assert.Equal(plant.Tanks.Count * 4, page.FindAll(".pid-canvas .pid-tank-limit").Count);
        Assert.NotNull(page.Find("svg.pid-minimap .pid-minimap-view"));
        Assert.Equal(4, page.FindAll(".pid-dock-tab").Count);
        Assert.Contains("%", page.Find(".pid-zoom").TextContent);
        Assert.NotNull(page.Find(".pid-toolbar button[title='Fit the whole diagram (0)']"));
    }

    [Fact]
    public async Task TankLevelsAndLimitLabelsAreRealSvgTextElements()
    {
        // Regression: Razor emitted "@:<text x=\"@...\">" lines as escaped text, so values were invisible in a browser.
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var page = host.Context.Render<Diagram>();

        Assert.Equal(host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Tanks.Count, page.FindAll("text.pid-value").Count(t => t.TextContent.EndsWith('%')));
        Assert.NotEmpty(page.FindAll("text.pid-tank-limit-label"));
        Assert.DoesNotContain("text-anchor=&quot;", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZoomButtonsFitAndZoomLabelFollowTheViewBox()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Diagram>();
        var initial = ViewBox(page);

        page.FindAll(".pid-toolbar .btn")[0].Click(); // zoom in
        var zoomedIn = ViewBox(page);
        Assert.True(zoomedIn.W < initial.W - 1, $"expected a smaller viewBox width, got {zoomedIn.W} after {initial.W}");

        page.FindAll(".pid-toolbar .btn")[1].Click(); // zoom out
        page.FindAll(".pid-toolbar .btn")[1].Click();
        var zoomedOut = ViewBox(page);
        Assert.True(zoomedOut.W > initial.W, $"expected a larger viewBox width, got {zoomedOut.W}");

        page.FindAll(".pid-toolbar .btn").Single(b => b.TextContent == "Fit").Click();
        var fitted = ViewBox(page);
        Assert.Equal(initial.W, fitted.W, 3);
        Assert.Equal(initial.X, fitted.X, 3);
        Assert.Contains("%", page.Find(".pid-zoom").TextContent);
    }

    [Fact]
    public async Task WheelZoomsAtTheCursorAndDragPansUsingThePointerModule()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        SetupPidModule(host, new PidPoint(100, 100), new PidPoint(150, 130));
        var page = host.Context.Render<Diagram>();
        var initial = ViewBox(page);
        var canvas = page.Find(".pid-canvas");

        canvas.TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100, ClientX = 100, ClientY = 100 });
        var zoomed = ViewBox(page);
        Assert.True(zoomed.W < initial.W - 1, $"wheel up should zoom in, width {initial.W} -> {zoomed.W}");
        // zoom kept the cursor point fixed: the view moved towards (100,100)
        Assert.True(zoomed.X > initial.X && zoomed.Y > initial.Y, "view should pan towards the cursor when zooming in");

        canvas.PointerDown(new PointerEventArgs { PointerId = 1, Button = 0, ClientX = 100, ClientY = 100 });
        canvas.PointerMove(new PointerEventArgs { PointerId = 1, ClientX = 150, ClientY = 130 });
        canvas.PointerUp(new PointerEventArgs { PointerId = 1, ClientX = 150, ClientY = 130 });
        var panned = ViewBox(page);
        Assert.Equal(zoomed.X - 50, panned.X, 3);
        Assert.Equal(zoomed.Y - 30, panned.Y, 3);
    }

    [Fact]
    public async Task MinimapClickCentresTheView()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Diagram>();
        var initial = ViewBox(page);

        page.Find("svg.pid-minimap").TriggerEvent("onclick", new MouseEventArgs { OffsetX = 150, OffsetY = 40 });
        var centred = ViewBox(page);
        Assert.NotEqual(initial.X, centred.X);
        Assert.True(centred.X > initial.X, "clicking the right side of the minimap should move the view right");
    }

    [Fact]
    public async Task ZoomingFarOutDropsValuesAndInstrumentsToKeepTheDiagramLegible()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Diagram>();
        Assert.NotEmpty(page.FindAll(".pid-canvas text.pid-value"));
        Assert.NotEmpty(page.FindAll(".pid-canvas g.pid-instrument"));

        for (var i = 0; i < 8; i++)
            page.FindAll(".pid-toolbar .btn")[1].Click(); // zoom out

        Assert.Empty(page.FindAll(".pid-canvas text.pid-value"));
        Assert.Empty(page.FindAll(".pid-canvas g.pid-instrument"));
        Assert.Equal(3, page.FindAll(".pid-canvas circle.pid-pump").Count);
        Assert.NotEmpty(page.FindAll(".pid-canvas .pid-device-label"));
    }

    [Fact]
    public async Task ActiveUnacknowledgedAlarmsGetABlinkingPriorityBell()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        Assert.True(host.Rig.Training.SetTankLevel("T-101", 99).Accepted);
        await host.Rig.StepAsync(3);
        var alarm = host.Rig.Alarms.GetCurrent().Single(a => a.IsActive && !a.IsAcknowledged);

        var page = host.Context.Render<Diagram>();
        var bell = page.FindAll(".pid-alarm-bell").Single();
        Assert.Contains("pid-p1", bell.ClassList); // high-high level alarm is critical
        Assert.Contains("pid-blink", bell.ClassList);

        Assert.True(host.Rig.Alarms.Acknowledge(alarm.Key, new Actor("eva", AccessRole.Operator)));
        host.Broadcaster.Publish(RealtimeTopics.Alarms, host.Rig.Alarms.GetCurrent());
        page.WaitForAssertion(() =>
            Assert.DoesNotContain("pid-blink", page.FindAll(".pid-alarm-bell").Single().GetAttribute("class")!), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task KeyboardShortcutsSwitchDockTabsAndEscapeClearsTheSelection()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();
        var workspace = page.Find(".pid-workspace");

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Tanks[0].Name).Click();
        Assert.Contains(plant.Tanks[0].Name, page.Find("aside.detail").TextContent);

        workspace.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "3" });
        Assert.Equal("true", page.FindAll(".pid-dock-tab")[2].GetAttribute("aria-selected"));
        Assert.NotNull(page.Find(".pid-command-table, .empty-state"));

        workspace.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        Assert.NotNull(page.Find("aside.pid-control-empty"));
        Assert.DoesNotContain(plant.Tanks[0].Name, page.Find("aside.detail").TextContent);
    }

    [Fact]
    public async Task DoubleClickOnADeviceOpensItsControlWindow()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        var valve = page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Valves[0].Name);
        valve.TriggerEvent("ondblclick", new MouseEventArgs());
        Assert.Contains(plant.Valves[0].Name, page.Find("aside.detail").TextContent);
    }
}
