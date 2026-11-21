using Application.Common;
using Application.Identity;
using Application.Realtime;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid;
using Hmi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridPagesTests
{
    [Theory]
    [InlineData(typeof(GridOverview), "Electrical distribution grid")]
    [InlineData(typeof(GridDiagram), "Single-line diagram")]
    [InlineData(typeof(GridFeeders), "North feeder")]
    [InlineData(typeof(GridTransformers), "Main transformer")]
    [InlineData(typeof(GridSwitches), "Feeder breaker")]
    [InlineData(typeof(GridLoads), "Town load")]
    [InlineData(typeof(GridProtection), "Feeder protection")]
    [InlineData(typeof(GridAlarms), "Bus voltage low")]
    [InlineData(typeof(GridMeasurements), "RTU1")]
    [InlineData(typeof(GridInstructor), "Grid instructor")]
    [InlineData(typeof(GridEditor), "Network editor")]
    [InlineData(typeof(GridScenarios), "Scenario catalog")]
    [InlineData(typeof(GridRules), "Grid detection rules")]
    public async Task EachGridPageRendersAFakeNetwork(Type component, string expected)
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render(b => { b.OpenComponent(0, component); b.CloseComponent(); });
        Assert.Contains(expected, page.Markup);
        Assert.DoesNotContain("Live data could not be refreshed", page.Markup);
    }

    [Theory]
    [InlineData(AccessRole.Operator, false)]
    [InlineData(AccessRole.Administrator, true)]
    public async Task MeasurementsHideGroundTruthFromOperators(AccessRole role, bool truth)
    {
        await using var host = HmiTestHost.Create(role, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridMeasurements>();
        Assert.Contains("19.40 kV", page.Markup);
        Assert.Equal(truth, page.Markup.Contains("True value", StringComparison.Ordinal));
        Assert.Equal(truth, page.Markup.Contains("Spoofed", StringComparison.Ordinal));
        Assert.Equal(truth, page.Markup.Contains("20.20 kV", StringComparison.Ordinal));
        var session = host.Context.Services.GetRequiredService<HmiSession>();
        Assert.Equal(truth, session.GridSnapshot().Measurements[0].IsSpoofed);
        Assert.Equal(truth ? 20.2 : 19.4, session.GridSnapshot().Measurements[0].TrueValue);
    }

    [Fact]
    public async Task FaceplatesHideInjectedFaultsFromOperators()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        foreach (var id in new[] { "Q1", "T1", "L1" })
        {
            var page = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, id));
            Assert.DoesNotContain("SwitchStuck", page.Markup);
            Assert.DoesNotContain("Overheating", page.Markup);
            Assert.DoesNotContain("EarthFault", page.Markup);
            Assert.DoesNotContain("spoofed", page.Markup);
        }
    }

    [Theory]
    [InlineData(typeof(GridInstructor))]
    [InlineData(typeof(GridEditor))]
    public async Task InstructorPagesDenyOperators(Type component)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render(b => { b.OpenComponent(0, component); b.CloseComponent(); });
        Assert.Contains("Access denied", page.Markup);
        Assert.Empty(page.FindAll("input, select, svg, button"));
        Assert.Empty(host.GridTraining.Calls);
        Assert.Empty(host.GridAdministration.Saves);
    }

    [Fact]
    public async Task PagesRefreshOnGridTopicsAndReleaseSubscriptions()
    {
        // Instructor view: operators see totals recomputed from reported measurements by GridViewRedactor.
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridOverview>();
        host.Grid.State = host.Grid.State with { Totals = host.Grid.State.Totals with { LoadMw = 9.75 } };
        host.Broadcaster.Publish(RealtimeTopics.Grid, 2);
        page.WaitForAssertion(() => Assert.Contains("9.75 MW", page.Markup));
        await host.Context.DisposeComponentsAsync();
        Assert.Equal(0, host.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task DiagramUsesDefinitionPositionsAndDistinctSwitchSymbols()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridDiagram>();
        Assert.Equal(host.Grid.Definition.Buses.Count + host.Grid.Definition.Sources.Count + host.Grid.Definition.Transformers.Count
            + host.Grid.Definition.Switches.Count + host.Grid.Definition.Loads.Count + host.Grid.Definition.Generators.Count + host.Grid.Definition.Capacitors.Count,
            page.FindAll("g.grid-node").Count);
        Assert.NotEmpty(page.FindAll(".deenergized"));
        Assert.Single(page.FindAll("rect.switch-closed"));
        Assert.Contains("tap 0", page.Markup);
        Assert.Contains("voltage-hv", page.Markup);
        Assert.Contains("voltage-mv", page.Markup);
        var bus = page.FindAll("g.grid-node").Single(n => n.GetAttribute("aria-label") == "MV bus");
        Assert.Equal("translate(80 280)", bus.GetAttribute("transform"));
        bus.Click();
        Assert.Contains("19.40 kV", page.Find(".grid-faceplate").TextContent);
        var before = page.Find("svg.grid-diagram").GetAttribute("viewBox");
        page.FindAll("button").Single(b => b.TextContent == "Zoom in").Click();
        Assert.NotEqual(before, page.Find("svg.grid-diagram").GetAttribute("viewBox"));
    }
}
