using Application.Common;
using Application.Grid;
using Application.Identity;
using Bunit;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid;
using Hmi.Services.Grid;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldTests
{
    private static (IRenderedComponent<GridSld> Page, List<string?> Selected, List<GridSldAction> Actions) RenderSld(
        HmiTestHost host, bool canOperate = true, string? selected = null, IReadOnlyList<GridTag>? tags = null, GridSnapshot? snapshot = null)
    {
        var selectedLog = new List<string?>();
        var actions = new List<GridSldAction>();
        var page = host.Context.Render<GridSld>(p => p
            .Add(x => x.Snapshot, snapshot ?? host.Grid.State)
            .Add(x => x.Definition, host.Grid.Definition)
            .Add(x => x.Tags, tags ?? [])
            .Add(x => x.CanOperate, canOperate)
            .Add(x => x.SelectedId, selected)
            .Add(x => x.SelectedIdChanged, EventCallback.Factory.Create<string?>(host, id => selectedLog.Add(id)))
            .Add(x => x.OnAction, EventCallback.Factory.Create<GridSldAction>(host, a => actions.Add(a))));
        return (page, selectedLog, actions);
    }

    private static GridSnapshot WithMeasurement(GridSnapshot state, GridMeasurementSnapshot measurement) =>
        state with { Measurements = [.. state.Measurements, measurement] };

    [Fact]
    public async Task RendersEveryElementKindWithSubstationFrameAndMinimap()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        var definition = host.Grid.Definition;
        var expected = definition.Buses.Count + definition.Sources.Count + definition.Lines.Count + definition.Transformers.Count
            + definition.Switches.Count + definition.Loads.Count + definition.Generators.Count + definition.Capacitors.Count;
        Assert.Equal(expected, page.FindAll("g.sld-el").Count);
        foreach (var kind in new[] { "bus", "source", "line", "transformer", "switch", "load", "generator", "capacitor" })
            Assert.NotEmpty(page.FindAll($"g.sld-el.kind-{kind}"));
        var frame = Assert.Single(page.FindAll(".sld-frame"));
        Assert.Contains("Station RTU", frame.TextContent);
        Assert.NotEmpty(page.FindAll("svg.sld-minimap .mm-wire"));
        Assert.NotEmpty(page.FindAll("svg.sld-minimap .mm-view"));
        Assert.NotEmpty(page.FindAll(".sld-search-input"));
    }

    [Fact]
    public async Task VoltageClassesDeenergisedAndLoadingClasses()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        Assert.Contains("v-110", page.Find("g.sld-el[data-id='B-HV']").GetAttribute("class"));
        Assert.Contains("v-20", page.Find("g.sld-el[data-id='B-MV']").GetAttribute("class"));
        // B-END is de-energised in the fake snapshot.
        Assert.Contains("is-dead", page.Find("g.sld-el[data-id='B-END']").GetAttribute("class"));
        // T1 loads at 42 % -> heat-ok; a branch over 100 % gets heat-over.
        Assert.Contains("heat-ok", page.Find("g.sld-el[data-id='T1'] .sld-wire").GetAttribute("class"));
        var overloaded = host.Grid.State with { Branches = [host.Grid.State.Branch("L1")! with { LoadingPercent = 130 }] };
        var (page2, _, _) = RenderSld(host, snapshot: overloaded);
        Assert.Contains("heat-over", page2.Find("g.sld-el[data-id='L1'] .sld-wire").GetAttribute("class"));
        var deadLine = host.Grid.State with { Branches = [host.Grid.State.Branch("L1")! with { Energized = false }] };
        var (page3, _, _) = RenderSld(host, snapshot: deadLine);
        Assert.Contains("is-dead", page3.Find("g.sld-el[data-id='L1'] .sld-wire").GetAttribute("class"));
    }

    [Fact]
    public async Task SwitchStatesClosedOpenAndInvalidQuality()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (plain, _, _) = RenderSld(host);
        Assert.Contains("closed", plain.Find("g.sld-el[data-id='Q1']").GetAttribute("class"));
        Assert.Contains("open", plain.Find("g.sld-el[data-id='QS1']").GetAttribute("class"));
        var bad = WithMeasurement(host.Grid.State,
            new GridMeasurementSnapshot("SW-QS1", "QS1", "RTU1", GridMeasurementKind.SwitchState, "", 0, MeasurementQuality.Bad, 1, false, null));
        var (page, _, _) = RenderSld(host, snapshot: bad);
        var invalid = page.Find("g.sld-el[data-id='QS1']");
        Assert.Contains("is-invalid", invalid.GetAttribute("class"));
        Assert.Contains("?", invalid.TextContent);
        // Q1 breaker renders a square symbol; disconnector a blade.
        Assert.NotEmpty(page.FindAll("g.sld-el[data-id='Q1'] rect.sym-switch"));
        Assert.NotEmpty(page.FindAll("g.sld-el[data-id='QS1'] path.sym"));
    }

    [Fact]
    public async Task TagBadgesRenderPerKind()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var tags = new List<GridTag>
        {
            new("t1", "Q1", GridTagKind.DoNotOperate, "rack work", "eva", DateTimeOffset.UtcNow),
            new("t2", "L1", GridTagKind.Earthed, "cable earthed", "eva", DateTimeOffset.UtcNow),
            new("t3", "LD1", GridTagKind.WorkInProgress, "meter swap", "eva", DateTimeOffset.UtcNow),
            new("t4", "T1", GridTagKind.Information, "AVR note", "eva", DateTimeOffset.UtcNow)
        };
        var (page, _, _) = RenderSld(host, tags: tags);
        Assert.Contains("DNO", page.Find("g.sld-el[data-id='Q1'] .sld-tag.tag-dno").TextContent);
        Assert.NotEmpty(page.FindAll("g.sld-el[data-id='L1'] .sld-tag.tag-earthed .tag-glyph"));
        Assert.Contains("WIP", page.Find("g.sld-el[data-id='LD1'] .sld-tag.tag-wip").TextContent);
        Assert.Contains("i", page.Find("g.sld-el[data-id='T1'] .sld-tag.tag-info").TextContent);
    }

    [Fact]
    public async Task AlarmBellBlinksUnacknowledgedAndProtectionFlagShows()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        // AL1: active, unacknowledged, High severity, source B-MV.
        var bell = page.Find("g.sld-el[data-id='B-MV'] .sld-alarm");
        Assert.Contains("p2", bell.GetAttribute("class"));
        Assert.Contains("blink-unack", bell.GetAttribute("class"));
        // P1 tripped and protects Q1.
        Assert.NotEmpty(page.FindAll("g.sld-el[data-id='Q1'] .sld-protection"));
        // Acknowledged alarms do not blink.
        var acked = host.Grid.State with { Alarms = [host.Grid.State.Alarms[0] with { Acknowledged = true, AcknowledgedBy = "eva" }] };
        var (page2, _, _) = RenderSld(host, snapshot: acked);
        Assert.DoesNotContain("blink-unack", page2.Find("g.sld-el[data-id='B-MV'] .sld-alarm").GetAttribute("class"));
    }

    [Fact]
    public async Task SelectionHaloAndRootAttribute()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host, selected: "Q1");
        var g = page.Find("g.sld-el[data-id='Q1']");
        Assert.Contains("is-selected", g.GetAttribute("class"));
        Assert.NotEmpty(page.FindAll("g.sld-el[data-id='Q1'] .sld-halo"));
        Assert.Equal("Q1", page.Find(".grid-sld").GetAttribute("data-selected"));
    }

    [Fact]
    public async Task ClickSelectsDoubleClickRaisesOpenControl()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, selected, actions) = RenderSld(host);
        page.Find("g.sld-el[data-id='B-MV']").Click();
        Assert.Equal("B-MV", Assert.Single(selected));
        page.Find("g.sld-el[data-id='L1']").DoubleClick();
        Assert.Equal(new GridSldAction("L1", GridSldActionKind.OpenControl), Assert.Single(actions));
    }

    [Fact]
    public async Task ContextMenuSwitchOperatorOffersSwitchingTagTrendDetails()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, actions) = RenderSld(host, canOperate: true);
        page.Find("g.sld-el[data-id='Q1']").ContextMenu(new MouseEventArgs { ClientX = 50, ClientY = 60 });
        var menu = page.Find(".sld-context-menu");
        var labels = menu.QuerySelectorAll(".sld-menu-item").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Open…", "Close…", "Place tag…", "Trend", "Details" }, labels);
        // Q1 is closed: Close… is disabled, Open… enabled.
        var close = menu.QuerySelectorAll(".sld-menu-item").Single(b => b.TextContent.Trim() == "Close…");
        Assert.True(close.HasAttribute("disabled"));
        menu.QuerySelectorAll(".sld-menu-item").Single(b => b.TextContent.Trim() == "Open…").Click();
        Assert.Equal(new GridSldAction("Q1", GridSldActionKind.SelectOpen), Assert.Single(actions));
        Assert.Empty(page.FindAll(".sld-context-menu"));
    }

    [Fact]
    public async Task ContextMenuLineViewerIsReadOnly()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        var (page, _, actions) = RenderSld(host, canOperate: false);
        page.Find("g.sld-el[data-id='L1']").ContextMenu(new MouseEventArgs { ClientX = 50, ClientY = 60 });
        var labels = page.Find(".sld-context-menu").QuerySelectorAll(".sld-menu-item").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Control window", "Trend", "Details" }, labels);
        page.Find(".sld-context-menu").QuerySelectorAll(".sld-menu-item").Single(b => b.TextContent.Trim() == "Control window").Click();
        Assert.Equal(new GridSldAction("L1", GridSldActionKind.OpenControl), Assert.Single(actions));

        var (page2, _, actions2) = RenderSld(host, canOperate: false);
        page2.Find("g.sld-el[data-id='Q1']").ContextMenu(new MouseEventArgs { ClientX = 50, ClientY = 60 });
        var switchLabels = page2.Find(".sld-context-menu").QuerySelectorAll(".sld-menu-item").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Trend", "Details" }, switchLabels);
        Assert.Empty(actions2);
    }

    [Fact]
    public async Task ContextMenuPlaceTagTrendDetailsRaiseActions()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, actions) = RenderSld(host);
        page.Find("g.sld-el[data-id='T1']").ContextMenu(new MouseEventArgs { ClientX = 10, ClientY = 10 });
        var menu = page.Find(".sld-context-menu");
        Assert.Contains("Place tag…", menu.TextContent);
        menu.QuerySelectorAll(".sld-menu-item").Single(b => b.TextContent.Trim() == "Place tag…").Click();
        Assert.Equal(new GridSldAction("T1", GridSldActionKind.PlaceTag), Assert.Single(actions));
        page.Find("g.sld-el[data-id='LD1']").ContextMenu(new MouseEventArgs { ClientX = 10, ClientY = 10 });
        page.Find(".sld-context-menu").QuerySelectorAll(".sld-menu-item").Single(b => b.TextContent.Trim() == "Trend").Click();
        Assert.Equal(new GridSldAction("LD1", GridSldActionKind.ShowTrend), actions[1]);
    }

    [Fact]
    public async Task KeyboardMovesSelectionAndRaisesActionsAndZooms()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, selected, actions) = RenderSld(host, selected: "Q1");
        var svg = page.Find("svg.sld-canvas");
        var fit = svg.GetAttribute("viewBox");
        svg.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.NotNull(Assert.Single(selected)); // moves off Q1 to the next element in reading order
        Assert.NotEqual("Q1", selected[0]);
        svg.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(new GridSldAction("Q1", GridSldActionKind.OpenControl), Assert.Single(actions));
        svg.KeyDown(new KeyboardEventArgs { Key = "+" });
        var zoomed = svg.GetAttribute("viewBox");
        Assert.NotEqual(fit, zoomed);
        svg.KeyDown(new KeyboardEventArgs { Key = "0" });
        Assert.Equal(fit, svg.GetAttribute("viewBox"));
        svg.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Null(selected[^1]);
    }

    [Fact]
    public async Task ZoomButtonsWheelAndFitChangeViewBox()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        var svg = page.Find("svg.sld-canvas");
        var fit = svg.GetAttribute("viewBox");
        page.Find("button[title='Zoom in (+)']").Click();
        var zoomedIn = svg.GetAttribute("viewBox");
        Assert.NotEqual(fit, zoomedIn);
        page.Find("button[title='Zoom out (−)']").Click();
        Assert.NotEqual(zoomedIn, svg.GetAttribute("viewBox"));
        svg.Wheel(new WheelEventArgs { DeltaY = -120, ClientX = 700, ClientY = 400 });
        Assert.NotEqual(fit, svg.GetAttribute("viewBox"));
        page.Find("button[title='Fit network (0)']").Click();
        Assert.Equal(fit, svg.GetAttribute("viewBox"));
    }

    [Fact]
    public async Task PointerDragPansTheCanvasThroughTheJsModule()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var module = host.Context.JSInterop.SetupModule("./_content/Hmi/js/sld.js");
        module.Setup<DiagramPosition>("point", c => c.Arguments.Count > 2 && (double)c.Arguments[1]! == 100.0).SetResult(new DiagramPosition(100, 100));
        module.Setup<DiagramPosition>("point", c => c.Arguments.Count > 2 && (double)c.Arguments[1]! == 140.0).SetResult(new DiagramPosition(140, 130));
        module.Setup<bool>("capture", _ => true).SetResult(true);
        module.Setup<bool>("release", _ => true).SetResult(true);
        var (page, _, _) = RenderSld(host);
        var svg = page.Find("svg.sld-canvas");
        var before = svg.GetAttribute("viewBox");
        svg.PointerDown(new PointerEventArgs { PointerId = 1, Button = 0, ClientX = 100, ClientY = 100 });
        svg.PointerMove(new PointerEventArgs { PointerId = 1, ClientX = 140, ClientY = 130 });
        svg.PointerUp(new PointerEventArgs { PointerId = 1, ClientX = 140, ClientY = 130 });
        Assert.NotEqual(before, svg.GetAttribute("viewBox"));
    }

    [Fact]
    public async Task LevelOfDetailHidesAndShowsLabelsWithZoom()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        // Fit view is the low LOD: ids of busbars/feeders only.
        Assert.NotEmpty(page.FindAll(".sld-lod-id"));
        Assert.Empty(page.FindAll(".sld-lod-name"));
        Assert.Empty(page.FindAll(".sld-value"));
        var zoomIn = page.Find("button[title='Zoom in (+)']");
        for (var i = 0; i < 4; i++)
            zoomIn.Click();
        // Medium LOD: names.
        Assert.NotEmpty(page.FindAll(".sld-lod-name"));
        Assert.Empty(page.FindAll(".sld-value"));
        for (var i = 0; i < 6; i++)
            zoomIn.Click();
        // High LOD: measurement value boxes.
        Assert.NotEmpty(page.FindAll(".sld-value"));
        Assert.Contains("20.20 kV", page.Markup); // B-MV voltage in the instructor snapshot
        Assert.Contains("80.0 A · 20 %", page.Markup); // L1 branch current/loading
        Assert.Contains("1.50 MW", page.Markup); // LD1 load
        Assert.Contains("tap 0", page.Markup); // transformer tap
    }

    [Fact]
    public async Task SearchFindsSelectsAndZoomsToElement()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, selected, _) = RenderSld(host);
        var svg = page.Find("svg.sld-canvas");
        var before = svg.GetAttribute("viewBox");
        page.Find(".sld-search-input").Input("town");
        var item = Assert.Single(page.FindAll(".sld-search-item"));
        Assert.Contains("LD1", item.TextContent);
        item.Click();
        Assert.Equal("LD1", Assert.Single(selected));
        Assert.NotEqual(before, svg.GetAttribute("viewBox"));
    }

    [Fact]
    public async Task QuickListNavigatesToSubstationAndFeeder()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, selected, _) = RenderSld(host);
        var svg = page.Find("svg.sld-canvas");
        var before = svg.GetAttribute("viewBox");
        page.Find(".sld-quicklist").Change("el:L1");
        Assert.Equal("L1", Assert.Single(selected));
        Assert.NotEqual(before, svg.GetAttribute("viewBox"));
        page.Find(".sld-quicklist").Change("rtu:RTU1");
        Assert.NotEqual(before, svg.GetAttribute("viewBox"));
    }

    [Fact]
    public async Task ViewModesAndLoadingHeatToggleApplyCanvasClasses()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        var svg = page.Find("svg.sld-canvas");
        Assert.Contains("mode-voltagelevels", svg.GetAttribute("class"));
        Button(page, "Loading").Click();
        Assert.Contains("mode-loading", svg.GetAttribute("class"));
        Button(page, "Energised").Click();
        Assert.Contains("mode-energisation", svg.GetAttribute("class"));
        page.Find(".sld-heat-toggle input").Change(true);
        Assert.Contains("heat-on", svg.GetAttribute("class"));
    }

    [Fact]
    public async Task MinimapClickNavigatesTheViewport()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        var before = page.Find("svg.sld-canvas").GetAttribute("viewBox");
        page.Find("svg.sld-minimap").PointerDown(new PointerEventArgs { PointerId = 2, ClientX = 100, ClientY = 65 });
        Assert.NotEqual(before, page.Find("svg.sld-canvas").GetAttribute("viewBox"));
    }

    [Fact]
    public async Task OperatorsOnlySeeReportedValuesNeverSpoofIndicators()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var redacted = GridViewRedactor.ForOperators(host.Grid.State, host.Grid.Definition);
        var (page, _, _) = RenderSld(host, snapshot: redacted);
        var zoomIn = page.Find("button[title='Zoom in (+)']");
        for (var i = 0; i < 10; i++)
            zoomIn.Click();
        Assert.NotEmpty(page.FindAll(".sld-value"));
        Assert.Contains("19.40 kV", page.Markup); // reported (spoofed) voltage of B-MV
        Assert.DoesNotContain("20.20 kV", page.Markup); // true value hidden
        Assert.NotEmpty(page.FindAll(".sld-qflag.q-u")); // uncertain quality flag shown
        Assert.DoesNotContain("poof", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HoverShowsTooltipWithNameAndValues()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var (page, _, _) = RenderSld(host);
        page.Find("g.sld-el[data-id='B-MV']").PointerOver(new PointerEventArgs { ClientX = 30, ClientY = 30 });
        var tooltip = page.Find(".sld-tooltip");
        Assert.Contains("MV bus", tooltip.TextContent);
        Assert.Contains("B-MV", tooltip.TextContent);
        page.Find("g.sld-el[data-id='B-MV']").PointerOut(new PointerEventArgs { ClientX = 30, ClientY = 30 });
        Assert.Empty(page.FindAll(".sld-tooltip"));
    }

    [Fact]
    public async Task PreviewRouteRendersTheCanvasFullScreen()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridSldPreview>();
        Assert.NotEmpty(page.FindAll("svg.sld-canvas"));
        Assert.NotEmpty(page.FindAll("g.sld-el"));
        Assert.DoesNotContain("Live data could not be refreshed", page.Markup);
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> page, string text) where T : IComponent =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
