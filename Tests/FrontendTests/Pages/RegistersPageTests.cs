using Application.Identity;
using Application.Registers;
using Bunit;
using Domain.Control;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class RegistersPageTests
{
    [Theory]
    [InlineData(AccessRole.Viewer, true)]
    [InlineData(AccessRole.Operator, false)]
    public async Task BrowserShowsRegistersButHidesDisallowedWrites(AccessRole role, bool writesEnabled)
    {
        await using var host = HmiTestHost.Create(role, registerOptions: new RegisterWriteOptions { WritesEnabled = writesEnabled });
        var page = host.Context.Render<Registers>();

        Assert.Contains("PLC-1", page.Markup);
        Assert.Contains("100", page.Find("tbody").TextContent);
        Assert.Empty(page.FindAll("button"));

        page.FindAll("select")[0].Change("2");
        page.FindAll("select")[1].Change("Input");
        var expected = host.Rig.Registers.ListControllers().Single(controller => controller.UnitId == 2)
            .Registers.Count(register => register.Area == RegisterArea.Input);
        Assert.Equal(expected, page.FindAll("tbody tr").Count);
        Assert.Empty(page.FindAll("button"));

        page.Find("input[type=checkbox]").Change(true);
        Assert.Equal(ControllerRegisterMap.Size, page.FindAll("tbody tr").Count);
    }

    [Fact]
    public async Task OperatorWritesHoldingSetpointAfterConfirming()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, registerOptions: new RegisterWriteOptions { WritesEnabled = true });
        var page = host.Context.Render<Registers>();
        var previous = host.Rig.Registers.Read(1, RegisterArea.Holding, 100, 1, "test").Values.Single();

        page.FindAll("button").Single(button => button.TextContent == "Edit 100").Click();
        page.Find("input[type=number]").Change("777");
        page.FindAll("button").Single(button => button.TextContent == "Write setpoint").Click();
        Assert.Contains("777", page.Find(".dialog").TextContent);
        Assert.Equal(previous, host.Rig.Registers.Read(1, RegisterArea.Holding, 100, 1, "test").Values.Single());

        page.Find(".dialog .btn-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Holding register 100 updated", page.Find(".banner-ok").TextContent));
        Assert.Equal((ushort)777, host.Rig.Registers.Read(1, RegisterArea.Holding, 100, 1, "test").Values.Single());
        page.FindAll("select")[1].Change("Input");
        Assert.Empty(page.FindAll("button"));
    }
}
