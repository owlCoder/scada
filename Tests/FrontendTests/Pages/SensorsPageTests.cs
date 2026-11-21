using System.Globalization;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class SensorsPageTests
{
    [Theory]
    [InlineData(AccessRole.Operator, false)]
    [InlineData(AccessRole.Administrator, true)]
    public async Task OnlyInstructorsSeeSpoofedSensorsTrueValue(AccessRole role, bool instructor)
    {
        await using var host = HmiTestHost.Create(role);
        Assert.True(host.Rig.Training.SpoofSensor("LT-101", 12.34).Accepted);
        var sensor = host.Rig.Training.Snapshot().Sensors.Single(sensor => sensor.Id == "LT-101");
        var trueReading = $"{sensor.TrueValue.ToString("0.00", CultureInfo.InvariantCulture)} {sensor.Unit}";

        var page = host.Context.Render<Sensors>();
        var row = page.FindAll("tbody tr").Single(row => row.QuerySelector("strong")?.TextContent == "LT-101");

        Assert.Contains($"12.34 {sensor.Unit}", row.QuerySelector(".reported-value")!.TextContent);
        Assert.Contains("Quality", page.Find("thead").TextContent);
        Assert.Contains("Health", page.Find("thead").TextContent);
        Assert.Contains("Fault", page.Find("thead").TextContent);
        Assert.Empty(page.FindAll("button"));
        if (instructor)
        {
            Assert.Equal(trueReading, row.QuerySelector(".true-value")!.TextContent.Trim());
            Assert.Contains("Active (12.34", row.TextContent);
            Assert.Contains("Drift", page.Markup);
        }
        else
        {
            Assert.DoesNotContain(trueReading, row.TextContent);
            Assert.DoesNotContain("True value", page.Markup);
            Assert.DoesNotContain("Spoofing", page.Markup);
            Assert.DoesNotContain("Drift", page.Markup);
        }
    }
}
