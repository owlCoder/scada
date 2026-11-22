using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Layout;
using Hmi.Services.Shell;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrontendTests.Shared;

public sealed class ShellStatusBarTests
{
    [Fact]
    public async Task ShowsConnectionUpdateAgeSelectedElementAndKeyboardHints()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        var bar = host.Context.Render<StatusBar>();

        Assert.Contains("Circuit connected", bar.Find(".statusbar").TextContent);
        Assert.Contains("0.0 s ago", bar.Find(".statusbar").TextContent);
        Assert.Contains("selected —", bar.Find(".status-selected").TextContent);
        var hints = bar.Find(".status-hints").TextContent;
        Assert.Contains("Ctrl+K", hints);
        Assert.Contains("F2", hints);
        Assert.Contains("F3", hints);
        Assert.Contains("Ctrl+Shift+A", hints);
        Assert.Contains("? help", hints);

        shell.Selected = "T-101";
        bar.WaitForAssertion(() => Assert.Contains("selected T-101", bar.Find(".status-selected").TextContent));

        host.Rig.Time.Advance(TimeSpan.FromSeconds(5));
        shell.Selected = "P-201";
        bar.WaitForAssertion(() => Assert.Contains("5.0 s ago", bar.Find(".statusbar").TextContent));

        shell.Connection = "Offline";
        bar.WaitForAssertion(() => Assert.Contains("Circuit offline", bar.Find(".statusbar").TextContent));
    }

    [Fact]
    public async Task HelpButtonAsksTheShellForTheShortcutOverlay()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        var help = false;
        shell.HelpRequested += () => help = true;
        var bar = host.Context.Render<StatusBar>();

        bar.Find(".btn-help").Click();

        Assert.True(help);
    }
}
