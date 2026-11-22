using Application.Common;
using Application.Identity;
using Application.Realtime;
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

public sealed class ShellAlarmBarTests
{
    private static GridAlarm Alarm(string key, AlarmSeverity severity, TimeSpan raised, bool acknowledged = false, string message = "Something") =>
        new(key, "SRC-1", GridAlarmType.Undervoltage, severity, message, raised, true, acknowledged, acknowledged ? "eva" : null);

    private static bool SetAlarms(HmiTestHost host, params GridAlarm[] alarms)
    {
        host.Grid.State = host.Grid.State with { Alarms = alarms };
        return true;
    }

    [Fact]
    public async Task UnacknowledgedAlarmsAreOrderedByPriorityThenNewestAndBlink()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        SetAlarms(host,
            Alarm("a-low", AlarmSeverity.Low, TimeSpan.FromSeconds(10), message: "low alarm"),
            Alarm("a-crit-old", AlarmSeverity.Critical, TimeSpan.FromSeconds(5), message: "critical old"),
            Alarm("a-crit-new", AlarmSeverity.Critical, TimeSpan.FromSeconds(30), message: "critical new"),
            Alarm("a-high", AlarmSeverity.High, TimeSpan.FromSeconds(20), message: "high alarm"),
            Alarm("a-acked", AlarmSeverity.Critical, TimeSpan.FromSeconds(40), acknowledged: true, message: "acked alarm"));
        var bar = host.Context.Render<AlarmBar>();

        var items = bar.FindAll(".alarm-item");
        Assert.Equal(4, items.Count);
        Assert.Contains("p1", items[0].ClassList);
        Assert.Contains("p1", items[1].ClassList);
        Assert.Contains("p2", items[2].ClassList);
        Assert.Contains("p4", items[3].ClassList);
        Assert.Contains("critical new", items[0].TextContent);
        Assert.Contains("critical old", items[1].TextContent);
        Assert.All(items, item => Assert.Contains("unack", item.ClassList)); // blink while unacknowledged
        Assert.DoesNotContain("acked alarm", bar.Markup);

        var counts = bar.FindAll(".alarm-count").Select(c => c.TextContent.Trim()).ToArray();
        Assert.Equal(["P1 2", "P2 1", "P3 0", "P4 1"], counts);
    }

    [Fact]
    public async Task AcknowledgeOnAnItemRequiresConfirmationAndCallsTheGridControl()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        SetAlarms(host, Alarm("AL9", AlarmSeverity.Critical, TimeSpan.FromSeconds(3), message: "Bus voltage low"));
        var bar = host.Context.Render<AlarmBar>();

        bar.FindAll(".alarm-ack").Single().Click();
        Assert.Empty(host.GridControl.Calls); // still waiting for confirmation
        bar.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();

        bar.WaitForAssertion(() => Assert.Contains(host.GridControl.Calls, call =>
            call.Command.Type == GridCommandType.AcknowledgeAlarm && call.Command.TargetId == "AL9" && call.Actor.Name == "eva" && call.Source == "hmi"));
        host.Broadcaster.Publish(RealtimeTopics.GridAlarms, 1);
        bar.WaitForAssertion(() => Assert.Contains("No unacknowledged alarms", bar.Find(".alarm-banner").TextContent));
    }

    [Fact]
    public async Task CancelDoesNotAcknowledgeTheAlarm()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        SetAlarms(host, Alarm("AL9", AlarmSeverity.High, TimeSpan.FromSeconds(3)));
        var bar = host.Context.Render<AlarmBar>();

        bar.FindAll(".alarm-ack").Single().Click();
        bar.FindAll(".dialog button").Single(b => b.TextContent == "Cancel").Click();

        Assert.Empty(host.GridControl.Calls);
        Assert.False(host.Grid.State.Alarms.Single(a => a.Key == "AL9").Acknowledged);
    }

    [Fact]
    public async Task AcknowledgeAllVisibleAcknowledgesEveryShownAlarm()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        SetAlarms(host,
            Alarm("A1", AlarmSeverity.Critical, TimeSpan.FromSeconds(1)),
            Alarm("A2", AlarmSeverity.High, TimeSpan.FromSeconds(2)),
            Alarm("A3", AlarmSeverity.Low, TimeSpan.FromSeconds(3)));
        var bar = host.Context.Render<AlarmBar>();

        bar.Find(".alarm-ack-all").Click();
        bar.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();

        bar.WaitForAssertion(() => Assert.Equal(3, host.GridControl.Calls.Count(c => c.Command.Type == GridCommandType.AcknowledgeAlarm)));
        Assert.Equal(["A1", "A2", "A3"], host.GridControl.Calls.Select(c => c.Command.TargetId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task ViewerSeesAlarmsButNoAcknowledgeButtons()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        SetAlarms(host, Alarm("AL9", AlarmSeverity.Critical, TimeSpan.FromSeconds(3), message: "Bus voltage low"));
        var bar = host.Context.Render<AlarmBar>();

        Assert.Single(bar.FindAll(".alarm-item"));
        Assert.Empty(bar.FindAll(".alarm-ack"));
        Assert.Empty(bar.FindAll(".alarm-ack-all"));
    }

    [Theory]
    [InlineData(SimulationMode.Grid, "/grid/alarms")]
    [InlineData(SimulationMode.Water, "/alarms")]
    public async Task ClickingAnItemOpensTheAlarmListAndSelectsTheAlarm(SimulationMode mode, string path)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: mode);
        if (mode == SimulationMode.Grid)
            SetAlarms(host, Alarm("AL9", AlarmSeverity.High, TimeSpan.FromSeconds(3)));
        else
        {
            host.Rig.Training.SetTankLevel("T-101", 99);
            await host.Rig.StepAsync();
        }
        var shell = host.Context.Services.GetRequiredService<ShellState>();
        var bar = host.Context.Render<AlarmBar>();

        bar.Find(".alarm-item-btn").Click();

        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith(path, navigation.Uri);
        Assert.Equal(mode == SimulationMode.Grid ? "AL9" : host.Rig.Alarms.GetCurrent().First(a => !a.IsAcknowledged).Key, shell.SelectedAlarmKey);
    }

    [Fact]
    public async Task WaterModeAcknowledgementGoesThroughTheAlarmService()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        host.Rig.Training.SetTankLevel("T-101", 99);
        await host.Rig.StepAsync();
        var alarm = host.Rig.Alarms.GetCurrent().First(a => !a.IsAcknowledged);
        var bar = host.Context.Render<AlarmBar>();

        bar.FindAll(".alarm-ack").Single().Click();
        Assert.False(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged);
        bar.FindAll(".dialog button").Single(b => b.TextContent == "Confirm").Click();

        bar.WaitForAssertion(() => Assert.True(host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).IsAcknowledged
            && host.Rig.Alarms.GetCurrent().Single(a => a.Key == alarm.Key).AcknowledgedBy == "eva"));
    }
}
