using Application.Identity;
using Bunit;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Hmi.Components.Shared;
using Xunit;

namespace FrontendTests.Shared;

public sealed class PlantFaceplateTests
{
    [Fact]
    public async Task ViewersSeeDeviceFactsWithoutCommands()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var faceplate = host.Context.Render<PlantFaceplate>(parameters => parameters.Add(faceplate => faceplate.DeviceId, "P-101"));

        var panel = faceplate.Find("aside.detail.plant-faceplate");
        Assert.Contains("Transfer Pump A", panel.TextContent);
        Assert.Contains("P-101", panel.TextContent);
        Assert.Empty(faceplate.FindAll("button"));
    }

    [Fact]
    public async Task PumpModeSwitchFromTheFaceplateRunsOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var faceplate = host.Context.Render<PlantFaceplate>(parameters => parameters.Add(faceplate => faceplate.DeviceId, "P-101"));

        faceplate.FindAll("button").Single(button => button.TextContent == "Manual").Click();
        Assert.Contains("Switch to manual", faceplate.Find(".dialog").TextContent);
        Assert.Equal(ControlMode.Automatic, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.Mode);

        faceplate.Find(".dialog .btn-primary").Click();

        faceplate.WaitForAssertion(() => Assert.Empty(faceplate.FindAll(".dialog")));
        var pump = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!;
        Assert.Equal(ControlMode.Manual, pump.Mode);
        Assert.Equal("eva", pump.LastChangedBy);
        Assert.Contains("banner-ok", faceplate.Markup);
    }

    [Fact]
    public async Task ValveFaceplateShowsTheSetpointMarkAndConfirmsPositionChanges()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var faceplate = host.Context.Render<PlantFaceplate>(parameters => parameters.Add(faceplate => faceplate.DeviceId, "V-101"));

        Assert.NotNull(faceplate.Find(".level-setpoint"));
        Assert.NotNull(faceplate.Find("input[type=number]"));

        faceplate.Find("input[type=number]").Change("80");
        faceplate.FindAll("button").Single(button => button.TextContent == "Set position").Click();
        Assert.Contains("Move V-101 to 80 %?", faceplate.Find(".dialog").TextContent);
        faceplate.FindAll(".dialog button").Single(button => button.TextContent == "Cancel").Click();
        Assert.Empty(faceplate.FindAll(".dialog"));
    }

    [Fact]
    public async Task UnknownDeviceShowsAnEmptyFaceplate()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var faceplate = host.Context.Render<PlantFaceplate>(parameters => parameters.Add(faceplate => faceplate.DeviceId, "X-999"));
        Assert.Contains("No device X-999", faceplate.Find("aside.detail").TextContent);
    }

    [Fact]
    public async Task DiagramSelectionOpensTheFaceplateAndKeyboardSelectionWorks()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var page = host.Context.Render<Diagram>();

        Assert.Equal(plant.Tanks.Count * 4, page.FindAll(".pid-canvas .pid-tank-limit").Count);
        Assert.Equal(plant.Valves.Count, page.FindAll(".pid-canvas .pid-valve-stem").Count);
        Assert.Equal(plant.Pumps.Count,
            page.FindAll(".pid-canvas .pid-pump-stop").Count + page.FindAll(".pid-canvas .pid-pump-arrow").Count + page.FindAll(".pid-canvas .pid-pump-fault-mark").Count);

        var pumpDevice = page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Pumps[0].Name);
        pumpDevice.TriggerEvent("onkeydown", new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        var faceplate = page.Find("aside.detail.plant-faceplate");
        Assert.Contains(plant.Pumps[0].Name, faceplate.TextContent);
        Assert.Contains("Start", faceplate.TextContent);
        Assert.Contains("Stop", faceplate.TextContent);
    }
}
