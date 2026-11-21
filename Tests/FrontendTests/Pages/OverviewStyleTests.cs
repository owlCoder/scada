using Application.Identity;
using Bunit;
using Domain.Process.Alarms;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class OverviewStyleTests
{
    [Fact]
    public async Task TankKpiCardsShowLevelSparklinesAndTheAlarmSummaryListsEveryPriority()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        await host.Rig.StepAsync(host.Rig.Engine.Options.TelemetryIntervalSteps * 3);
        var page = host.Context.Render<Overview>();

        var cards = page.FindAll(".kpi-card");
        Assert.Equal(5, cards.Count);
        Assert.Equal(3, page.FindAll(".kpi-card .sparkline").Count);
        Assert.Equal(3, page.FindAll(".kpi-card polyline.sparkline-line").Count);
        Assert.All(page.FindAll(".kpi-card [role=meter]"), meter => Assert.NotNull(meter.GetAttribute("aria-valuenow")));
        Assert.Contains("%", page.Find(".kpi-value .unit").TextContent);

        var rows = page.FindAll(".alarm-summary-row");
        Assert.Equal(Enum.GetValues<AlarmSeverity>().Length, rows.Count);
        Assert.Equal(["Critical", "High", "Medium", "Low"], rows.Select(row => row.QuerySelector(".pill")!.TextContent));
        Assert.Contains("Alarm list", page.Markup);
    }

    [Fact]
    public async Task AbnormalTankAndActiveAlarmHighlightTheKpiCardsInAlarmColour()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        host.Rig.Training.SetTankLevel("T-101", 99);
        await host.Rig.StepAsync();
        var page = host.Context.Render<Overview>();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".kpi-card.kpi-alarm"));
            var alarmRow = page.FindAll(".alarm-summary-row").Single(row => row.QuerySelector(".pill-alarm") is not null);
            Assert.NotEqual("0", alarmRow.QuerySelector(".alarm-summary-count")!.TextContent);
            Assert.Contains("Active alarms", page.Markup);
        });
    }
}
