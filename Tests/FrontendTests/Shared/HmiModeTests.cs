using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Layout;
using Hmi.Components.Pages;
using Hmi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrontendTests.Shared;

public sealed class HmiModeTests
{
    [Theory]
    [InlineData(SimulationMode.Water, "Plant diagram", "Single-line diagram")]
    [InlineData(SimulationMode.Grid, "Single-line diagram", "Plant diagram")]
    public async Task ModeClaimSelectsNavigation(SimulationMode mode, string present, string absent)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: mode);
        var page = host.Context.Render<NavMenu>();
        Assert.Contains(present, page.Markup); Assert.DoesNotContain(absent, page.Markup);
        Assert.Contains("Events", page.Markup); Assert.Contains("Scenarios", page.Markup); Assert.Contains("My account", page.Markup);
        Assert.Equal(mode, host.Context.Services.GetRequiredService<HmiSession>().Mode);
    }

    [Fact]
    public async Task GridClaimRedirectsTheRootToGridOverview()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        host.Context.Render<Overview>();
        Assert.EndsWith("/grid", host.Context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(SimulationMode.Water, "Electrical distribution grid")]
    [InlineData(SimulationMode.Grid, "Water treatment plant")]
    public async Task TopBarPostsModeSwitchAndShowsCurrentModeAlarms(SimulationMode mode, string next)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: mode);
        var page = host.Context.Render<TopBar>();
        Assert.Equal("/account/mode", page.Find("form.mode-switch").GetAttribute("action"));
        Assert.Equal("post", page.Find("form.mode-switch").GetAttribute("method"));
        Assert.Contains(next, page.Find("form.mode-switch").TextContent);
        Assert.Equal(mode == SimulationMode.Grid, page.Find(".alarm-banner").TextContent.Contains("Bus voltage low", StringComparison.Ordinal));
    }
}
