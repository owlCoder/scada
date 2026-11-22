using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Shared;
using Hmi.Services.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrontendTests.Shared;

public sealed class ShellQuickSwitchTests
{
    [Fact]
    public async Task GridModeOffersElementIdsAndPublishesRequestFocus()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        string? focused = null;
        shell.FocusRequested += id => focused = id;
        var switcher = host.Context.Render<QuickSwitch>();

        switcher.Find(".quick-input").Input("Q1");

        var item = switcher.FindAll(".quick-item").Single(i => i.TextContent.Contains("Q1"));
        Assert.Contains("quick-element", item.ClassList);
        item.Click();

        Assert.Equal("Q1", focused);
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/grid/diagram", navigation.Uri);
    }

    [Fact]
    public async Task FilteringByDisplayNameNavigatesToTheDisplay()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var switcher = host.Context.Render<QuickSwitch>();

        switcher.Find(".quick-input").Input("single-line");

        var item = switcher.FindAll(".quick-item").Single();
        Assert.Contains("Single-line diagram", item.TextContent);
        item.Click();
        Assert.EndsWith("/grid/diagram", host.Context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task WaterModeSwitchesToWaterDisplays()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var switcher = host.Context.Render<QuickSwitch>();

        switcher.Find(".quick-input").Input("tank");

        var item = switcher.FindAll(".quick-item").Single(i => i.TextContent.Contains("Tanks"));
        item.Click();
        Assert.EndsWith("/tanks", host.Context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task EnterChoosesTheFirstMatch()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var switcher = host.Context.Render<QuickSwitch>();

        switcher.Find(".quick-input").Input("alarms");
        switcher.Find(".quick-input").KeyDown("Enter");

        Assert.EndsWith("/grid/alarms", host.Context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task OperatorsDoNotSeeAdministrationDisplaysButInstructorsDo()
    {
        await using var operatorHost = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var operatorSwitch = operatorHost.Context.Render<QuickSwitch>();
        operatorSwitch.Find(".quick-input").Input("editor");
        Assert.Contains("No matches.", operatorSwitch.Markup);

        await using var admin = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var adminSwitch = admin.Context.Render<QuickSwitch>();
        adminSwitch.Find(".quick-input").Input("editor");
        Assert.Contains("Network editor", adminSwitch.Markup);
    }

    [Fact]
    public async Task EscapeClosesTheSwitcherThroughOnClose()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var closed = false;
        var switcher = host.Context.Render<QuickSwitch>(parameters => parameters
            .Add(s => s.OnClose, () =>
            {
                closed = true;
                return Task.CompletedTask;
            }));

        switcher.Find(".quick-input").KeyDown("Escape");

        Assert.True(closed);
    }
}
