using Application.Identity;
using Bunit;
using Domain.Control;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class ControllersPageTests
{
    [Fact]
    public async Task OperatorsSeeControllersAndSetpointsWithoutControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        Assert.True(host.Rig.Training.ForceRegister("PLC-1", RegisterArea.Input, 250, 4321));
        var page = host.Context.Render<Controllers>();

        Assert.Contains("PLC-1", page.Markup);
        Assert.Contains("RTU-1", page.Markup);
        Assert.Contains("Scan cycle / count", page.Markup);
        Assert.Contains("Setpoints and forced registers", page.Markup);
        Assert.Contains("4321", page.Find("table").TextContent);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task StopRunsOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        var page = host.Context.Render<Controllers>();

        page.FindAll("button").First(button => button.TextContent == "Stop").Click();
        Assert.Contains("PLC-1", page.Find(".dialog").TextContent);
        Assert.Equal(ControllerState.Running, host.Rig.Engine.Read(world => world.FindController("PLC-1")!.State));

        page.Find(".dialog .btn-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Controller stopped", page.Find(".banner-ok").TextContent));
        Assert.Equal(ControllerState.Stopped, host.Rig.Engine.Read(world => world.FindController("PLC-1")!.State));
    }
}
