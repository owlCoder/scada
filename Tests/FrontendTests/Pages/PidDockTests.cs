using Application.Commands;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PidDockTests
{
    private static async Task RaiseAlarmAsync(HmiTestHost host)
    {
        Assert.True(host.Rig.Training.SetTankLevel("T-101", 99).Accepted);
        await host.Rig.StepAsync(3);
    }

    [Fact]
    public async Task AlarmsTabListsAlarmsWithPriorityAndAcknowledgesAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        await RaiseAlarmAsync(host);
        var alarm = host.Rig.Alarms.GetCurrent().Single(a => a.IsActive && !a.IsAcknowledged);
        var page = host.Context.Render<Diagram>();

        // Alarms is the default tab; the badge shows the unacknowledged count.
        Assert.Equal("1", page.Find(".pid-dock-badge").TextContent);
        var row = page.FindAll(".pid-alarm-table tbody tr").Single();
        Assert.Contains("pid-p1", row.QuerySelector(".pid-prio")!.GetAttribute("class"));
        Assert.Contains("pid-blink", row.QuerySelector(".pid-prio")!.GetAttribute("class"));
        Assert.Contains(alarm.Message, row.TextContent);

        row.QuerySelectorAll("button").Single(b => b.TextContent == "Acknowledge").Click();
        Assert.Contains("Acknowledge alarm?", page.Find(".pid-dock .dialog").TextContent);
        Assert.False(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged);

        page.FindAll(".pid-dock .dialog button").Single(b => b.TextContent == "Confirm").Click();
        page.WaitForAssertion(() =>
            Assert.True(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged), TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".pid-dock-badge")), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ViewersCannotAcknowledgeFromTheDock()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await RaiseAlarmAsync(host);
        var page = host.Context.Render<Diagram>();

        Assert.NotEmpty(page.FindAll(".pid-alarm-table tbody tr"));
        Assert.Empty(page.FindAll(".pid-alarm-table button"));
    }

    [Fact]
    public async Task EventsAndCommandsTabsShowLiveLogAndCommandOutcomes()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var actor = new Actor("eva", AccessRole.Operator);
        Assert.True(host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.SetManual, null, CommandSource.Hmi), actor).Accepted);
        host.Rig.Commands.Execute(new OperatorCommand("P-101", OperatorCommandType.StartPump, null, CommandSource.Hmi), new Actor("spy", AccessRole.Viewer));
        await host.Rig.StepAsync(3);
        var page = host.Context.Render<Diagram>();

        page.FindAll(".pid-dock-tab").Single(t => t.TextContent.Contains("Events")).Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".pid-event-table tbody tr")), TimeSpan.FromSeconds(3));
        Assert.Contains("Audit", page.Find(".pid-event-table").TextContent);

        page.FindAll(".pid-dock-tab").Single(t => t.TextContent.Contains("Commands")).Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".pid-command-table tbody tr").Count), TimeSpan.FromSeconds(3));
        var rows = page.FindAll(".pid-command-table tbody tr");
        Assert.Contains(rows, r => r.TextContent.Contains("Accepted") && r.TextContent.Contains("P-101"));
        Assert.Contains(rows, r => r.TextContent.Contains("Rejected") && r.TextContent.Contains("spy"));
        Assert.Contains(rows, r => r.TextContent.Contains("AuthorizationDenied") || r.TextContent.Contains("rejected"));
    }

    [Fact]
    public async Task TrendTabChartsUpToFourSelectedTags()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await host.Rig.StepAsync(10);
        var page = host.Context.Render<Diagram>();

        page.FindAll(".pid-dock-tab").Single(t => t.TextContent.Contains("Trend")).Click();
        var selects = page.FindAll(".pid-trend-picker select");
        Assert.Equal(4, selects.Count);
        Assert.True(selects[0].QuerySelectorAll("option").Count() > 5); // LT/FT/PT tags from the historian

        selects[0].Change("LT-101");
        selects = page.FindAll(".pid-trend-picker select");
        selects[1].Change("FT-101");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".pid-trend-line").Count), TimeSpan.FromSeconds(3));
        Assert.Contains("LT-101", page.Find(".pid-trend-legend").TextContent);
        Assert.Contains("FT-101", page.Find(".pid-trend-legend").TextContent);
        Assert.Equal(2, page.FindAll(".pid-trend-legend .pid-trend-key").Count);
    }
}
