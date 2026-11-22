using Application.Commands;
using Application.Identity;
using Bunit;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PidControlWindowTests
{
    [Fact]
    public async Task TankWindowShowsLevelGaugeLimitsInflowOutflowAndSparkline()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await host.Rig.StepAsync(5);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Tanks[0].Name).Click();

        var window = page.Find("aside.detail");
        Assert.Contains(plant.Tanks[0].Name, window.TextContent);
        Assert.Contains("Inflow / outflow", window.TextContent);
        Assert.Contains("LL", window.TextContent);
        Assert.Contains("HH", window.TextContent);
        Assert.NotNull(window.QuerySelector(".level")); // level gauge
        Assert.NotNull(window.QuerySelector(".sparkline")); // trend from the historian
    }

    [Fact]
    public async Task PumpWindowLetsAnOperatorStartThePumpAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var actor = new Actor("eva", AccessRole.Operator);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.SetManual, null, CommandSource.Hmi), actor).Accepted);
        await host.Rig.StepAsync(2);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Pumps[0].Name).Click();
        var window = page.Find("aside.detail");
        Assert.Contains("Transfer Pump A", window.TextContent);
        Assert.NotNull(window.QuerySelector(".sparkline"));

        window.QuerySelectorAll("button").Single(b => b.TextContent == "Start").Click();
        Assert.Contains("Start P-101?", page.Find(".dialog").TextContent);
        Assert.Equal(PumpState.Stopped, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.State);

        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".dialog")));
        await host.Rig.StepAsync(40);
        Assert.Equal(PumpState.Running, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.State);
        Assert.Contains("banner-ok", page.Markup);
    }

    [Fact]
    public async Task ViewerSeesThePumpWindowReadOnly()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Pumps[0].Name).Click();
        var window = page.Find("aside.detail");
        Assert.Contains(plant.Pumps[0].Name, window.TextContent);
        Assert.Empty(window.QuerySelectorAll("button"));
    }

    [Fact]
    public async Task ValveWindowHasASliderAndAppliesThePositionAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var actor = new Actor("eva", AccessRole.Operator);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("V-101", OperatorCommandType.SetManual, null, CommandSource.Hmi), actor).Accepted);
        await host.Rig.StepAsync(2);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Valves[0].Name).Click();
        var window = page.Find("aside.detail");
        var slider = window.QuerySelector("input.pid-slider[type=range]")!;
        Assert.Equal("100", slider.GetAttribute("value")); // V-101 starts fully open

        slider.Input("80");
        window.QuerySelectorAll("button").Single(b => b.TextContent == "Set position").Click();
        Assert.Contains("Move V-101 to 80 %?", page.Find(".dialog").TextContent);
        page.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".dialog")));
        Assert.Equal(80, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Valve("V-101")!.Setpoint);
    }

    [Fact]
    public async Task SensorWindowShowsReportedValueAndQualityOnlyForOperators()
    {
        await using var viewer = HmiTestHost.Create(AccessRole.Operator);
        await using var instructor = HmiTestHost.Create(AccessRole.Administrator);
        foreach (var host in new[] { viewer, instructor })
        {
            Assert.True(host.Rig.Training.SpoofSensor("LT-101", 99).Accepted);
            await host.Rig.StepAsync(2);
        }
        var plant = viewer.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var sensor = plant.Sensors.Single(s => s.Id == "LT-101");

        var viewerPage = viewer.Context.Render<Diagram>();
        viewerPage.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == sensor.Name).Click();
        var viewerWindow = viewerPage.Find("aside.detail");
        Assert.Contains("Reported", viewerWindow.TextContent);
        Assert.Contains("99", viewerWindow.TextContent);
        Assert.Contains("Quality", viewerWindow.TextContent);
        Assert.Contains("%", viewerWindow.TextContent);
        Assert.NotNull(viewerWindow.QuerySelector(".sparkline"));
        Assert.DoesNotContain("True value", viewerWindow.TextContent);

        var instructorPage = instructor.Context.Render<Diagram>();
        instructorPage.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == sensor.Name).Click();
        Assert.Contains("True value", instructorPage.Find("aside.detail").TextContent);
    }

    [Fact]
    public async Task ControllerWindowShowsStateScanAndComms()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Diagram>();

        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == "Transfer Station PLC").Click();

        var window = page.Find("aside.detail");
        Assert.Contains("PLC-1", window.TextContent);
        Assert.Contains("State", window.TextContent);
        Assert.Contains("Field comms", window.TextContent);
        Assert.Contains("Supervisory comms", window.TextContent);
        Assert.Contains("Scan", window.TextContent);
        Assert.Contains("Actuators", window.TextContent);
        Assert.Empty(window.QuerySelectorAll("button"));
    }
}
