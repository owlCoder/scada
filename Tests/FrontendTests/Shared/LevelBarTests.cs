using Bunit;
using Hmi.Components.Shared;
using Xunit;

namespace FrontendTests.Shared;

public sealed class LevelBarTests
{
    [Fact]
    public Task RendersMeterWithLimitAndSetpointMarks()
    {
        using var context = new BunitContext();
        var bar = context.Render<LevelBar>(parameters => parameters
            .Add(bar => bar.Percent, 50)
            .Add(bar => bar.Label, "T-101 level")
            .Add(bar => bar.Low, 20)
            .Add(bar => bar.High, 85)
            .Add(bar => bar.Setpoint, 60));

        var meter = bar.Find("[role=meter]");
        Assert.Equal("50", meter.GetAttribute("aria-valuenow"));
        Assert.Equal("T-101 level", meter.GetAttribute("aria-label"));
        Assert.NotNull(bar.Find(".level-fill"));
        Assert.Empty(bar.FindAll(".level-fill-alarm"));
        Assert.Equal(2, bar.FindAll(".level-mark").Count);
        Assert.Single(bar.FindAll(".level-mark-high"));
        Assert.Single(bar.FindAll(".level-setpoint"));
        Assert.Contains("50.0 %", bar.Markup);
        return Task.CompletedTask;
    }

    [Fact]
    public Task FillTurnsAlarmOnlyOutsideTheLimits()
    {
        using var context = new BunitContext();
        var high = context.Render<LevelBar>(parameters => parameters
            .Add(bar => bar.Percent, 97).Add(bar => bar.High, 85).Add(bar => bar.Low, 20));
        Assert.NotNull(high.Find(".level-fill-alarm"));
        Assert.Equal("97", high.Find("[role=meter]").GetAttribute("aria-valuenow"));

        var low = context.Render<LevelBar>(parameters => parameters
            .Add(bar => bar.Percent, 5).Add(bar => bar.High, 85).Add(bar => bar.Low, 20));
        Assert.NotNull(low.Find(".level-fill-alarm"));

        var inside = context.Render<LevelBar>(parameters => parameters
            .Add(bar => bar.Percent, 50).Add(bar => bar.High, 85).Add(bar => bar.Low, 20));
        Assert.NotNull(inside.Find(".level-fill"));
        Assert.Empty(inside.FindAll(".level-fill-alarm"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task NoMarksAreDrawnWithoutLimitsOrSetpoint()
    {
        using var context = new BunitContext();
        var bar = context.Render<LevelBar>(parameters => parameters.Add(bar => bar.Percent, 30));
        Assert.Empty(bar.FindAll(".level-mark"));
        Assert.Empty(bar.FindAll(".level-setpoint"));
        return Task.CompletedTask;
    }
}
