using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class DiagramPageTests
{
    [Fact]
    public async Task DiagramDrawsEveryDeviceAndShowsDetailsOnSelection()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());

        var page = host.Context.Render<Diagram>();

        Assert.Equal(plant.Tanks.Count, page.FindAll(".pid-canvas rect.pid-tank").Count);
        Assert.Equal(plant.Pumps.Count, page.FindAll(".pid-canvas circle.pid-pump").Count);
        Assert.Equal(plant.Valves.Count, page.FindAll(".pid-canvas polygon.pid-valve").Count);
        page.FindAll("g.pid-device").First(g => g.GetAttribute("aria-label") == plant.Pumps[0].Name).Click();
        Assert.Contains(plant.Pumps[0].Name, page.Find("aside.detail").TextContent);
    }

    [Fact]
    public async Task OperatorsSeeSpoofedReadingsWhileInstructorsSeeTheTruth()
    {
        await using var viewer = HmiTestHost.Create(AccessRole.Operator);
        await using var instructor = HmiTestHost.Create(AccessRole.Administrator);
        foreach (var host in new[] { viewer, instructor })
        {
            Assert.True(host.Rig.Training.SpoofSensor("LT-101", 99).Accepted);
            await host.Rig.StepAsync();
        }

        var operatorView = viewer.Context.Render<Diagram>().Find("text.pid-value").TextContent;
        var instructorView = instructor.Context.Render<Diagram>().Find("text.pid-value").TextContent;

        Assert.Equal("99.0 %", operatorView);
        Assert.NotEqual("99.0 %", instructorView);
    }
}
