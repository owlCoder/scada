using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class TrendsPageTests
{
    [Fact]
    public async Task TagPickerAndChartUpdateFromReportedTelemetry()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Trends>();
        Assert.Contains("Waiting for telemetry", page.Markup);

        Assert.True(host.Rig.Training.SpoofSensor("LT-101", 12.34).Accepted);
        await host.Rig.StepAsync(host.Rig.Engine.Options.TelemetryIntervalSteps);
        page.WaitForAssertion(() => Assert.NotNull(page.Find("option[value='LT-101']")));

        Assert.Equal(3, page.FindAll("select").Count);
        page.Find("select").Change("LT-101");
        Assert.Contains("12.34", page.Find(".chart").TextContent);
        Assert.Equal(2, page.FindAll(".chart .axis").Count);
        Assert.NotNull(page.Find(".chart circle.series"));
        Assert.Empty(page.FindAll("button"));

        Assert.True(host.Rig.Training.SpoofSensor("LT-101", 23.45).Accepted);
        await host.Rig.StepAsync(host.Rig.Engine.Options.TelemetryIntervalSteps);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("23.45", page.Find(".chart").TextContent);
            Assert.NotNull(page.Find(".chart polyline.series"));
        });
    }
}
