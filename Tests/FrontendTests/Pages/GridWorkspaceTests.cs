using Application.Common;
using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Realtime;
using Bunit;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid.Workspace;
using Hmi.Services.Grid;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridWorkspaceTests
{
    [Theory]
    [InlineData("Q1", "Switch", "Select open", "Feeder protection", "Reset protection")]
    [InlineData("T1", "Transformer", "Raise tap", "AVR", "HV voltage")]
    [InlineData("L1", "Line / cable", "rating", "P / Q from", "P / Q to")]
    [InlineData("LD1", "Load", "Customers", "Shed load", "Demand")]
    [InlineData("C1", "Capacitor bank", "Set steps", "Reactive power", "+ step")]
    [InlineData("B-MV", "Bus", "Limits", "Voltage within limits", "pu")]
    [InlineData("PV1", "Generator", "Connected", "P / Q", "Photovoltaic")]
    public async Task ControlWindowShowsTheSectionOfEachElementType(string id, string heading, string a, string b, string c)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridControlWindow>(p => p.Add(x => x.ElementId, id));
        var window = page.Find(".control-window");
        Assert.Contains(heading, window.TextContent);
        Assert.Contains(a, window.TextContent);
        Assert.Contains(b, window.TextContent);
        Assert.Contains(c, window.TextContent);
    }

    [Fact]
    public async Task ControlWindowHeaderShowsIdNameStateAndTagBadges()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var tagging = host.GridTagging!;
        await tagging.PlaceTagAsync("Q1", GridTagKind.DoNotOperate, "maintenance", new Actor("eva", AccessRole.Operator), default);
        var page = host.Context.Render<GridControlWindow>(p => p
            .Add(x => x.ElementId, "Q1")
            .Add(x => x.Tags, tagging.Tags)
            .Add(x => x.Tagging, tagging));
        var head = page.Find(".cw-head");
        Assert.Contains("Q1", head.TextContent);
        Assert.Contains("Feeder breaker", head.TextContent);
        Assert.Contains("Closed", head.TextContent);
        Assert.Contains("DoNotOperate", head.TextContent);
    }

    [Fact]
    public async Task SelectBeforeOperateFlowRunsWithCountdownConfirmExecuteAndCancel()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridControlWindow>(p => p.Add(x => x.ElementId, "Q1"));
        Button(page, "Select open").Click();
        Assert.Empty(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Assert.Equal(GridCommandType.SelectOpen, host.GridControl.Calls[0].Command.Type);
        page.WaitForAssertion(() => Assert.Contains("s remaining", page.Markup));
        Assert.True(Button(page, "Execute").HasAttribute("disabled") == false);
        Button(page, "Execute").Click();
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Equal(2, host.GridControl.Calls.Count));
        Assert.Equal(GridCommandType.Execute, host.GridControl.Calls[1].Command.Type);
        Assert.False(host.Grid.State.Switch("Q1")!.Closed);
    }

    [Fact]
    public async Task CancelSelectionReleasesTheSwitchWithoutOperating()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridControlWindow>(p => p.Add(x => x.ElementId, "Q1"));
        Button(page, "Select close").Click();
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Button(page, "Cancel selection").Click();
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Equal(GridCommandType.Cancel, host.GridControl.Calls.Last().Command.Type));
        Assert.True(host.Grid.State.Switch("Q1")!.Closed);
        Assert.False(host.Grid.State.Switch("Q1")!.Selected);
    }

    [Fact]
    public async Task TaggedSwitchIsDisabledWithTheBlockingTagShown()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var tagging = host.GridTagging!;
        await tagging.PlaceTagAsync("L1", GridTagKind.WorkInProgress, "crew on the feeder", new Actor("eva", AccessRole.Operator), default);
        // Q1 connects line L1: a WorkInProgress tag on L1 blocks the breaker.
        var page = host.Context.Render<GridControlWindow>(p => p
            .Add(x => x.ElementId, "Q1")
            .Add(x => x.Tags, tagging.Tags)
            .Add(x => x.Tagging, tagging));
        page.WaitForAssertion(() => Assert.Contains("Tagged WorkInProgress on L1", page.Markup));
        Assert.True(Button(page, "Select open").HasAttribute("disabled"));
        Assert.True(Button(page, "Select close").HasAttribute("disabled"));
        Assert.True(Button(page, "Execute").HasAttribute("disabled"));
    }

    [Fact]
    public async Task OfflineRtuDisablesSwitchingWithReason()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        host.Grid.State = host.Grid.State with
        {
            Alarms = [.. host.Grid.State.Alarms, new GridAlarm("AL-RTU", "RTU1", GridAlarmType.CommunicationLoss, AlarmSeverity.High,
                "RTU communication lost", TimeSpan.FromSeconds(1), true, false, null)]
        };
        var page = host.Context.Render<GridControlWindow>(p => p.Add(x => x.ElementId, "Q1"));
        page.WaitForAssertion(() => Assert.Contains("Owning RTU is offline", page.Markup));
        Assert.True(Button(page, "Select open").HasAttribute("disabled"));
    }

    [Fact]
    public async Task PlaceAndRemoveTagCallsGoThroughConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var tagging = host.GridTagging!;
        var page = host.Context.Render<GridControlWindow>(p => p
            .Add(x => x.ElementId, "Q1")
            .Add(x => x.Tags, tagging.Tags)
            .Add(x => x.Tagging, tagging));
        Button(page, "Place tag").Click();
        page.Find(".tag-form input[type=text]").Change("earth switch maintenance");
        Button(page, "Place tag").Click();
        Assert.Empty(tagging.PlaceCalls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(tagging.PlaceCalls));
        Assert.Equal(("Q1", GridTagKind.DoNotOperate, "earth switch maintenance"), (tagging.PlaceCalls[0].ElementId, tagging.PlaceCalls[0].Kind, tagging.PlaceCalls[0].Reason));
        Assert.Equal("eva", tagging.PlaceCalls[0].Actor.Name);

        var withTag = host.Context.Render<GridControlWindow>(p => p
            .Add(x => x.ElementId, "Q1")
            .Add(x => x.Tags, tagging.Tags)
            .Add(x => x.Tagging, tagging));
        Button(withTag, "Remove").Click();
        Button(withTag, "Confirm").Click();
        withTag.WaitForAssertion(() => Assert.Single(tagging.RemoveCalls));
        Assert.Equal("TAG1", tagging.RemoveCalls[0].TagId);
        Assert.Empty(tagging.Tags);
    }

    [Fact]
    public async Task CanvasSelectOpenActionStartsTheSboFlowInTheControlWindow()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("Q1", GridSldActionKind.SelectOpen)));
        page.WaitForAssertion(() => Assert.Contains("SelectOpen: Q1?", page.Markup));
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Assert.Equal(GridCommandType.SelectOpen, host.GridControl.Calls[0].Command.Type);
        page.WaitForAssertion(() => Assert.Contains("s remaining", page.Find(".control-window").TextContent));
    }

    [Fact]
    public async Task CanvasOpenControlShowDetailsAndPlaceTagActionsDriveTheDocks()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("T1", GridSldActionKind.ShowDetails)));
        page.WaitForAssertion(() => Assert.Contains("Details", page.Find(".control-window").TextContent));
        page.WaitForAssertion(() => Assert.Contains("RTU", page.Find(".control-window").TextContent));

        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("Q1", GridSldActionKind.PlaceTag)));
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".tag-form")));

        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("L1", GridSldActionKind.OpenControl)));
        page.WaitForAssertion(() => Assert.Equal("L1", page.Find(".workspace-canvas .grid-sld").GetAttribute("data-selected")));
        page.WaitForAssertion(() => Assert.Contains("North feeder", page.Find(".control-window").TextContent));
    }

    [Fact]
    public async Task CanvasShowTrendActionOpensTrendTabWithTheElementSeries()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        host.GridHistory!.Seed("V1", 19, .1, 60);
        host.GridHistory.Seed("I1", 70, .5, 60);
        var page = host.Context.Render<GridWorkspace>();
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("L1", GridSldActionKind.ShowTrend)));
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".trend-chart")));
        // The trend tab pre-selected the measurements of L1 (I1).
        var legend = page.Find(".trend-legend").TextContent;
        Assert.Contains("I1", legend);
        Assert.Single(page.FindAll(".trend-chart polyline"));
        // Toggling another measurement adds a second series.
        var checkboxes = page.FindAll(".trend-pick input");
        Assert.Equal(2, checkboxes.Count);
        page.FindAll(".trend-pick input").Single(c => c.Parent!.TextContent.Contains("V1")).Change(true);
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".trend-chart polyline").Count));
    }

    [Fact]
    public async Task TrendTabLimitsSelectionToFourSeries()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        foreach (var id in new[] { "M1", "M2", "M3", "M4", "M5" })
            host.GridHistory!.Seed(id, 1, .1, 30);
        var page = host.Context.Render<GridWorkspace>();
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("L1", GridSldActionKind.ShowTrend)));
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".trend-pick input").Count));
        for (var i = 0; i < 4; i++)
            page.FindAll(".trend-pick input")[i].Change(true);
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".trend-chart polyline").Count));
        Assert.True(page.FindAll(".trend-pick input")[4].HasAttribute("disabled"));
    }

    [Fact]
    public async Task AlarmAcknowledgeFromTheBottomDockRequiresConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        Assert.Contains("Bus voltage low", page.Markup);
        Button(page, "Acknowledge").Click();
        Assert.Empty(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        var command = host.GridControl.Calls[0].Command;
        Assert.Equal(GridCommandType.AcknowledgeAlarm, command.Type);
        Assert.Equal("AL1", command.TargetId);
        Assert.Equal("eva", host.Grid.State.Alarms[0].AcknowledgedBy);
    }

    [Fact]
    public async Task AlarmRowClickSelectsTheElementInTheWorkspace()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        page.FindAll(".dock-table .linklike").Single(b => b.TextContent.Trim() == "B-MV").Click();
        page.WaitForAssertion(() => Assert.Equal("B-MV", page.Find(".workspace-canvas .grid-sld").GetAttribute("data-selected")));
        page.WaitForAssertion(() => Assert.Contains("MV bus", page.Find(".control-window").TextContent));
    }

    [Fact]
    public async Task EventsAndCommandsTabsShowOnlyGridSourceEvents()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        await host.Rig.EventStore.AppendAsync(
        [
            SecurityEvent.Create(host.Rig.Time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information, "grid:hmi", "SelectOpen: Q1: None: ok", "eva", "Q1"),
            SecurityEvent.Create(host.Rig.Time.GetUtcNow(), EventCategories.DeviceChange, EventSeverity.Information, "grid:engine", "Breaker opened", "grid", "Q1"),
            SecurityEvent.Create(host.Rig.Time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information, "modbus:plc", "unrelated water event", "plc")
        ], default);
        var page = host.Context.Render<GridWorkspace>();
        Button(page, "Events").Click();
        page.WaitForAssertion(() => Assert.Contains("Breaker opened", page.Markup));
        page.WaitForAssertion(() => Assert.Contains("SelectOpen: Q1", page.Markup));
        Assert.DoesNotContain("unrelated water event", page.Markup);

        // Live: a newly published grid event appears on the open tab without reloading the page.
        await host.Rig.EventStore.AppendAsync(
            [SecurityEvent.Create(host.Rig.Time.GetUtcNow(), EventCategories.DeviceChange, EventSeverity.Information, "grid:engine", "Live trend event", "grid", "L1")], default);
        host.Broadcaster.Publish(RealtimeTopics.Events, 1);
        page.WaitForAssertion(() => Assert.Contains("Live trend event", page.Markup));

        Button(page, "Commands").Click();
        page.WaitForAssertion(() => Assert.Contains("SelectOpen: Q1", page.Markup));
        Assert.DoesNotContain("Breaker opened", page.Markup);
    }

    [Fact]
    public async Task TagsTabListsActiveTagsAndRemovesWithConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var tagging = host.GridTagging!;
        await tagging.PlaceTagAsync("Q1", GridTagKind.Earthed, "earthing applied", new Actor("eva", AccessRole.Operator), default);
        var page = host.Context.Render<GridWorkspace>();
        Button(page, "Tags").Click();
        page.WaitForAssertion(() => Assert.Contains("earthing applied", page.Markup));
        Assert.Contains("Earthed", page.Markup);
        Button(page, "Remove").Click();
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(tagging.RemoveCalls));
        Assert.Empty(tagging.Tags);
    }

    [Fact]
    public async Task KeyboardShortcutsDeselectOpenControlAndCancelCountdown()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("Q1", GridSldActionKind.OpenControl)));
        page.WaitForAssertion(() => Assert.Equal("Q1", page.Find(".workspace-canvas .grid-sld").GetAttribute("data-selected")));

        // Enter keeps the control window open (collapse it first, Enter reopens).
        page.Find(".dock-right .dock-toggle").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".dock-right.collapsed")));
        await page.InvokeAsync(() => page.Instance.OnWorkspaceKey("Enter"));
        page.WaitForAssertion(() => Assert.Null(page.FindAll(".dock-right.collapsed").FirstOrDefault()));

        // Delete cancels a running selection countdown (with confirmation).
        await page.InvokeAsync(() => page.Instance.HandleAction(new GridSldAction("Q1", GridSldActionKind.SelectOpen)));
        page.WaitForAssertion(() => Assert.Contains("SelectOpen: Q1?", page.Markup));
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        await page.InvokeAsync(() => page.Instance.OnWorkspaceKey("Delete"));
        page.WaitForAssertion(() => Assert.Contains("Cancel selection: Q1?", page.Markup));
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Equal(GridCommandType.Cancel, host.GridControl.Calls.Last().Command.Type));

        // Escape deselects.
        await page.InvokeAsync(() => page.Instance.OnWorkspaceKey("Escape"));
        page.WaitForAssertion(() => Assert.True(string.IsNullOrEmpty(page.Find(".workspace-canvas .grid-sld").GetAttribute("data-selected"))));
        page.WaitForAssertion(() => Assert.Contains("Select an element", page.Markup));
    }

    [Fact]
    public async Task DockCollapseTogglesAndResizeUpdateTheLayout()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        Assert.NotNull(page.Find(".dock-right:not(.collapsed)"));
        page.Find(".dock-right .dock-toggle").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".dock-right.collapsed")));
        page.Find(".dock-bottom .dock-toggle").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".dock-bottom.collapsed")));
        await page.InvokeAsync(() => page.Instance.OnBottomResize(300));
        // Reopening shows the docks again.
        page.Find(".dock-bottom .dock-toggle").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".dock-bottom:not(.collapsed)")));
        Assert.NotNull(page.Find(".dock-tabs"));
    }

    [Fact]
    public async Task ViewerSeesReadOnlyWorkspace()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridWorkspace>();
        Assert.DoesNotContain("Acknowledge", page.Markup);
        page.FindAll(".dock-table .linklike").Single(b => b.TextContent.Trim() == "B-MV").Click();
        page.WaitForAssertion(() => Assert.Contains("MV bus", page.Find(".control-window").TextContent));
        Assert.DoesNotContain(page.FindAll(".control-window button"), b =>
            b.TextContent.Contains("Select open") || b.TextContent.Contains("Execute") || b.TextContent.Contains("Place tag") || b.TextContent.Contains("Remove"));
        Assert.Empty(host.GridControl.Calls);
    }

    [Fact]
    public async Task MissingOptionalServicesHideTagAndTrendSections()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid, workspaceServices: false);
        var page = host.Context.Render<GridWorkspace>();
        page.FindAll(".dock-table .linklike").Single(b => b.TextContent.Trim() == "B-MV").Click();
        page.WaitForAssertion(() => Assert.Contains("MV bus", page.Find(".control-window").TextContent));
        Assert.DoesNotContain("<h3>Tags</h3>", page.Markup);
        Button(page, "Trend").Click();
        page.WaitForAssertion(() => Assert.Contains("History is not available.", page.Markup));
        Assert.DoesNotContain("Live data could not be refreshed", page.Markup);
    }

    [Fact]
    public async Task ControlWindowShowsMiniTrendsFromHistory()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        host.GridHistory!.Seed("V1", 19, .1, 60);
        var page = host.Context.Render<GridControlWindow>(p => p
            .Add(x => x.ElementId, "B-MV")
            .Add(x => x.History, host.GridHistory));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".cw-measurement .sparkline-mini polyline")));
        Assert.Contains("Uncertain", page.Find(".control-window").TextContent);
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> page, string text) where T : IComponent =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
