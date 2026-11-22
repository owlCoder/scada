using Application.Common;
using Application.Identity;
using Bunit;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using FrontendTests.Support;
using Hmi.Components.Layout;
using Hmi.Services.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrontendTests.Shared;

public sealed class ShellLayoutTests
{
    private static BunitJSInterop SetupShellModule(HmiTestHost host)
    {
        var module = host.Context.JSInterop.SetupModule("./_content/Hmi/js/shell.js");
        module.Setup<bool>("registerKeys", _ => true).SetResult(true);
        module.Setup<bool>("watchConnection", _ => true).SetResult(true);
        return module;
    }

    private static ShellKeyPress Key(string key, bool ctrl = false, bool shift = false, bool alt = false, bool meta = false) =>
        new(key, ctrl, shift, alt, meta);

    [Fact]
    public async Task MainLayoutLoadsTheShellModuleAndRegistersKeysAndConnectionWatch()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var module = SetupShellModule(host);

        var layout = host.Context.Render<MainLayout>();

        Assert.Contains(module.Invocations, call => call.Identifier == "registerKeys");
        Assert.Contains(module.Invocations, call => call.Identifier == "watchConnection");
        Assert.NotNull(layout.Find(".topbar"));
        Assert.NotNull(layout.Find(".alarm-banner"));
        Assert.NotNull(layout.Find(".rail"));
        Assert.NotNull(layout.Find(".statusbar"));
    }

    [Fact]
    public async Task KeyboardShortcutsDriveOverlaysAndNavigation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        SetupShellModule(host);
        var layout = host.Context.Render<MainLayout>();
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();

        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("k", ctrl: true)));
        Assert.NotNull(layout.Find(".quick-switch"));

        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("Escape")));
        Assert.Throws<Bunit.ElementNotFoundException>(() => layout.Find(".quick-switch"));

        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("?")));
        Assert.NotNull(layout.Find(".shortcut-help"));
        Assert.Contains("Ctrl", layout.Find(".shortcut-help").TextContent);
        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("Escape")));
        Assert.Throws<Bunit.ElementNotFoundException>(() => layout.Find(".shortcut-help"));

        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("F2")));
        Assert.EndsWith("/grid/alarms", navigation.Uri);
        await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("F3")));
        Assert.EndsWith("/grid/diagram", navigation.Uri);

        Assert.False(await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("x"))));
    }

    [Fact]
    public async Task CtrlShiftAAcknowledgesTheSelectedAlarmAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        SetupShellModule(host);
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        host.Grid.State = host.Grid.State with
        {
            Alarms = [new GridAlarm("AL9", "B-MV", GridAlarmType.Undervoltage, AlarmSeverity.High, "Bus voltage low", TimeSpan.FromSeconds(3), true, false, null)]
        };
        var layout = host.Context.Render<MainLayout>();
        shell.SelectedAlarmKey = "AL9";

        Assert.True(await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("a", ctrl: true, shift: true))));
        Assert.Empty(host.GridControl.Calls); // confirmation required first
        layout.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();

        layout.WaitForAssertion(() => Assert.Contains(host.GridControl.Calls, call =>
            call.Command.Type == GridCommandType.AcknowledgeAlarm && call.Command.TargetId == "AL9" && call.Actor.Name == "eva"));
    }

    [Fact]
    public async Task CtrlShiftAIsIgnoredForViewers()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        SetupShellModule(host);
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        var layout = host.Context.Render<MainLayout>();
        shell.SelectedAlarmKey = "AL1";

        Assert.False(await layout.InvokeAsync(() => layout.Instance.HandleKey(Key("a", ctrl: true, shift: true))));
        Assert.Empty(host.GridControl.Calls);
        Assert.Throws<Bunit.ElementNotFoundException>(() => layout.Find(".dialog"));
    }

    [Theory]
    [InlineData(SimulationMode.Water, "/diagram", true)]
    [InlineData(SimulationMode.Water, "/tanks", false)]
    [InlineData(SimulationMode.Grid, "/grid/diagram", true)]
    [InlineData(SimulationMode.Grid, "/grid", false)]
    public async Task WorkspaceDisplaysGetTheFullContentArea(SimulationMode mode, string path, bool workspace)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: mode);
        SetupShellModule(host);
        var layout = host.Context.Render<MainLayout>();
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo(path);

        layout.WaitForAssertion(() => Assert.Equal(workspace, layout.Find("main").ClassList.Contains("content-workspace")));
    }
}
