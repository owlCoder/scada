using Application.Telemetry;
using Bunit;
using Hmi.Components.Shared;
using Xunit;

namespace FrontendTests.Shared;

public sealed class SparklineTests
{
    private static TelemetryPoint Point(double value) =>
        new(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero), TimeSpan.Zero, value, "Good");

    [Fact]
    public Task MultiplePointsRenderAsAnInlineSvgPolyline()
    {
        using var context = new BunitContext();
        var sparkline = context.Render<Sparkline>(parameters => parameters
            .Add(sparkline => sparkline.Points, new[] { Point(10), Point(40), Point(25) })
            .Add(sparkline => sparkline.Label, "Recent level of T-101 (%)"));

        var svg = sparkline.Find("svg.sparkline");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Recent level of T-101 (%)", svg.GetAttribute("aria-label"));
        var polyline = sparkline.Find("polyline.sparkline-line");
        Assert.Equal(3, polyline.GetAttribute("points")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Empty(sparkline.FindAll("circle.sparkline-point"));
        Assert.Empty(sparkline.FindAll(".sparkline-alarm"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ASinglePointRendersAsADot()
    {
        using var context = new BunitContext();
        var sparkline = context.Render<Sparkline>(parameters => parameters
            .Add(sparkline => sparkline.Points, new[] { Point(12.5) }));

        Assert.Single(sparkline.FindAll("circle.sparkline-point"));
        Assert.Empty(sparkline.FindAll("polyline.sparkline-line"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task NoPointsRenderAnEmptyLabelledSvgWithoutASeries()
    {
        using var context = new BunitContext();
        var sparkline = context.Render<Sparkline>(parameters => parameters
            .Add(sparkline => sparkline.Points, Array.Empty<TelemetryPoint>())
            .Add(sparkline => sparkline.Label, "Trend"));

        Assert.NotNull(sparkline.Find("svg.sparkline-empty"));
        Assert.Empty(sparkline.FindAll("polyline.sparkline-line"));
        Assert.Empty(sparkline.FindAll("circle.sparkline-point"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AlarmStateSwitchesTheStrokeColourClass()
    {
        using var context = new BunitContext();
        var sparkline = context.Render<Sparkline>(parameters => parameters
            .Add(sparkline => sparkline.Points, new[] { Point(1), Point(2) })
            .Add(sparkline => sparkline.Alarm, true));

        Assert.NotNull(sparkline.Find("svg.sparkline-alarm"));
        Assert.NotNull(sparkline.Find("polyline.sparkline-line"));
        return Task.CompletedTask;
    }
}
