using Application.Identity;
using Bunit;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PumpsPageTests
{
    [Theory]
    [InlineData(AccessRole.Operator, false)]
    [InlineData(AccessRole.Administrator, true)]
    public async Task OnlyInstructorsSeeInjectedFaultsAndBypass(AccessRole role, bool seesTruth)
    {
        await using var host = HmiTestHost.Create(role);
        host.Rig.Training.InjectFault(new Domain.Process.Commands.FaultInjection("P-101", PumpFault: PumpFault.Seized));
        host.Rig.Training.SetInterlockBypass(true, "scenario");
        var page = host.Context.Render<Pumps>();
        Assert.Equal(seesTruth, page.Markup.Contains("Seized", StringComparison.Ordinal));
        var overview = host.Context.Render<Overview>();
        Assert.Equal(seesTruth, overview.Markup.Contains("BYPASSED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ViewersSeePumpsButNoCommands()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);

        var page = host.Context.Render<Pumps>();

        Assert.Contains("P-101", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task CommandsRunOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var page = host.Context.Render<Pumps>();

        page.FindAll("button").First(b => b.TextContent.Trim() == "Manual").Click();
        Assert.Contains("Switch to manual", page.Find(".dialog").TextContent);
        Assert.Equal(ControlMode.Automatic, host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!.Mode);

        page.Find(".dialog .btn-primary").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".dialog")));
        var pump = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot()).Pump("P-101")!;
        Assert.Equal(ControlMode.Manual, pump.Mode);
        Assert.Equal("eva", pump.LastChangedBy);
        Assert.Contains("banner-ok", page.Markup);
    }

    [Fact]
    public async Task CancellingADialogDoesNothing()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var page = host.Context.Render<Pumps>();

        page.FindAll("button").First(b => b.TextContent.Trim() == "Stop").Click();
        page.FindAll(".dialog button").First(b => b.TextContent == "Cancel").Click();

        Assert.Empty(page.FindAll(".dialog"));
        Assert.DoesNotContain("banner", page.Markup);
    }
}
