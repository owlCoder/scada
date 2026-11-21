using Application.Identity;
using Application.Scenarios;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class ScenariosPageTests
{
    [Fact]
    public async Task OperatorSeesRunHistoryAndDetectionsWithoutControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var run = await host.Rig.RunScenarioAsync("sensor-spoofing");

        var page = host.Context.Render<Scenarios>();

        Assert.Contains("Scenario catalog", page.Markup);
        Assert.Contains(run.RunId.ToString(), page.Markup);
        Assert.Contains(run.Outcome.ToString(), page.Markup);
        Assert.Contains("Expected detections", page.Markup);
        Assert.Contains("Observed detections", page.Markup);
        Assert.Contains("mass-balance-inconsistency", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task AdministratorStartsAndPausesOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        var page = host.Context.Render<Scenarios>();

        page.FindAll("button").First(button => button.TextContent == "Start").Click();
        Assert.Null(host.Rig.Scenarios.Current);
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Equal(ScenarioRunState.Running, host.Rig.Scenarios.Current?.State));
        Assert.NotNull(page.Find("progress"));

        page.FindAll("button").First(button => button.TextContent == "Pause").Click();
        Assert.Equal(ScenarioRunState.Running, host.Rig.Scenarios.Current?.State);
        page.Find(".dialog .btn-danger").Click();

        page.WaitForAssertion(() => Assert.Equal(ScenarioRunState.Paused, host.Rig.Scenarios.Current?.State));
        Assert.Contains("Resume", page.Markup);
    }
}
