using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Layout;
using Xunit;

namespace FrontendTests.Shared;

public sealed class ShellRailTests
{
    [Fact]
    public async Task WaterRailIsGroupedProcessOperationsSystem()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var rail = host.Context.Render<Rail>();

        Assert.Equal(["Process", "Operations", "System"], rail.FindAll(".rail-group").Select(g => g.TextContent.Trim()).ToArray());
        var hrefs = rail.FindAll(".rail-item").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains("", hrefs);
        Assert.Contains("diagram", hrefs);
        Assert.Contains("tanks", hrefs);
        Assert.Contains("alarms", hrefs);
        Assert.Contains("events", hrefs);
        Assert.Contains("scenarios", hrefs);
        Assert.Contains("health", hrefs);
        Assert.Contains("identity", hrefs);
        Assert.DoesNotContain("accounts", hrefs); // Administration group is hidden from operators
        Assert.Contains("Plant diagram", rail.Find("a[aria-label='Plant diagram']").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task GridRailListsGridDisplays()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var rail = host.Context.Render<Rail>();

        var hrefs = rail.FindAll(".rail-item").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains("grid", hrefs);
        Assert.Contains("grid/diagram", hrefs);
        Assert.Contains("grid/feeders", hrefs);
        Assert.Contains("grid/switches", hrefs);
        Assert.Contains("grid/alarms", hrefs);
        Assert.Contains("grid/scenarios", hrefs);
        Assert.DoesNotContain("accounts", hrefs);
        Assert.Contains("Single-line diagram", rail.Find("a[aria-label='Single-line diagram']").GetAttribute("aria-label"));
        Assert.DoesNotContain("Plant diagram", rail.Markup);
    }

    [Fact]
    public async Task AdministratorSeesTheAdministrationGroup()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var rail = host.Context.Render<Rail>();

        Assert.Equal(["Process", "Operations", "System", "Administration"], rail.FindAll(".rail-group").Select(g => g.TextContent.Trim()).ToArray());
        var hrefs = rail.FindAll(".rail-item").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains("grid/instructor", hrefs);
        Assert.Contains("grid/editor", hrefs);
        Assert.Contains("grid/rules", hrefs);
        Assert.Contains("accounts", hrefs);
    }

    [Fact]
    public async Task CurrentDisplayIsHighlightedInTheRail()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Water);
        var rail = host.Context.Render<Rail>();
        Assert.Contains("active", rail.Find("a[aria-label='Overview']").ClassList); // bUnit starts at /
    }

    [Fact]
    public async Task RailLabelsAreShortAndKeepTheFullTitleAsTooltipAndAriaLabel()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var rail = host.Context.Render<Rail>();
        var labels = rail.FindAll(".rail-label").Select(l => l.TextContent.Trim()).ToArray();
        Assert.All(labels, label => Assert.InRange(label.Length, 1, 9)); // never truncated in the 56 px rail
        var sld = rail.Find("a[aria-label='Single-line diagram']");
        Assert.Equal("Diagram", sld.QuerySelector(".rail-label")!.TextContent.Trim());
        Assert.Equal("Single-line diagram", sld.GetAttribute("title"));
        Assert.Equal("Single-line diagram", sld.GetAttribute("aria-label"));
        var transformers = rail.Find("a[aria-label='Transformers']");
        Assert.Equal("Xformers", transformers.QuerySelector(".rail-label")!.TextContent.Trim());
    }

    [Fact]
    public Task EveryDisplayEntryOfBothModesHasAShortRailLabel()
    {
        foreach (var mode in new[] { Application.Common.SimulationMode.Water, Application.Common.SimulationMode.Grid })
            foreach (var entry in Hmi.Services.Shell.DisplayCatalog.For(mode))
                Assert.InRange(entry.ShortLabel.Length, 1, 9);
        return Task.CompletedTask;
    }
}
