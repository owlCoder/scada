using System.Text.RegularExpressions;
using Application.Simulation;
using Application.Training;
using EndToEndTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace EndToEndTests.Hmi;

public sealed class HmiBrowserTests(ITestOutputHelper output)
{
    [Fact]
    public Task LoginRejectsWrongPasswordAcceptsOperatorAndLogsOut() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        await page.GotoAsync("/account/login");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SCADA training plant" })).ToBeVisibleAsync();
        await page.GetByLabel("Username").FillAsync("operator");
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync("wrong-test-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Invalid username or password.");
        await BrowserTestSession.LoginAsync(page, "operator");
        await Assertions.Expect(page.Locator(".topbar")).ToContainTextAsync("operator · Operator");
        await BrowserTestSession.NavigateAsync(page, "My account");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/account/login$"));
        Assert.DoesNotContain(await page.Context.CookiesAsync(), cookie => cookie.Name == "scada.session");
        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByLabel("Username")).ToBeVisibleAsync();
    }, output);

    [Fact]
    public Task AdministratorCanVisitEveryNavigationLinkWithoutConsoleOrCspErrors() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        var diagnostics = new BrowserDiagnostics();
        await diagnostics.WatchAsync(page);
        await BrowserTestSession.LoginAsync(page, "administrator");
        var links = await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link).AllTextContentsAsync();
        Assert.Equal(17, links.Count);
        foreach (var link in links)
        {
            await BrowserTestSession.NavigateAsync(page, link.Trim());
            await diagnostics.AssertCleanAsync(page);
        }
    }, output);

    [Fact]
    public Task OverviewAndDiagramValuesChangeThroughTheOpenBlazorCircuit() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(page, "operator");
        // No navigation or reload between reads: only circuit messages can update these DOM values.
        var overview = page.Locator(".metric strong").First;
        var first = await overview.InnerTextAsync();
        await Assertions.Expect(overview).Not.ToHaveTextAsync(first);
        await BrowserTestSession.NavigateAsync(page, "Plant diagram");
        var level = page.Locator("svg.diagram text.value").First;
        first = (await level.TextContentAsync())!;
        await Assertions.Expect(level).Not.ToHaveTextAsync(first);
        Assert.True(session.Factory.Services.GetRequiredService<SimulationEngine>().Status.StepCount > 1);
    }, output);

    [Fact]
    public Task OperatorConfirmsPumpStartAndStopAndCancellationLeavesItStopped() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(page, "operator");
        await BrowserTestSession.NavigateAsync(page, "Pumps");
        var pump = Row(page, "P-101");
        await pump.GetByRole(AriaRole.Button, new() { Name = "Manual", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(pump.Locator("td").Nth(2)).ToHaveTextAsync("Manual");
        await pump.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(pump.Locator("td").Nth(1)).ToHaveTextAsync("Stopped");

        await pump.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alertdialog)).ToContainTextAsync("P-101");
        await page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0);
        await Assertions.Expect(pump.Locator("td").Nth(1)).ToHaveTextAsync("Stopped");
        Assert.Equal(Domain.Process.Devices.PumpState.Stopped,
            session.Factory.Services.GetRequiredService<IPlantView>().Snapshot().Pump("P-101")!.State);

        await pump.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(pump.Locator("td").Nth(1)).ToHaveTextAsync("Running");
        await pump.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(pump.Locator("td").Nth(1)).ToHaveTextAsync("Stopped");
        await BrowserTestSession.NavigateAsync(page, "Events");
        await page.GetByLabel("Actor", new() { Exact = true }).FillAsync("operator");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { NameRegex = new Regex("^StartPump P-101:") })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { NameRegex = new Regex("^StopPump P-101:") }).First).ToBeVisibleAsync();
    }, output);

    [Fact]
    public Task ViewerHasNoOperateButtonsAndOperatorCannotOpenAccounts() => BrowserTestSession.RunAsync(async session =>
    {
        var viewer = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(viewer, "viewer");
        await Assertions.Expect(viewer.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true })).ToHaveCountAsync(0);
        foreach (var label in new[] { "Pumps", "Valves", "PLC / RTU", "Alarms", "Scenarios", "Registers" })
        {
            await BrowserTestSession.NavigateAsync(viewer, label);
            await Assertions.Expect(viewer.Locator("button.btn-danger, button.btn-primary, td button")).ToHaveCountAsync(0);
            await Assertions.Expect(viewer.GetByRole(AriaRole.Button, new()
            {
                NameRegex = new Regex("^(Start|Stop|Pause|Resume|Reset|Reset environment|Replay|Acknowledge|Manual|Automatic|Set %|Write setpoint)$")
            })).ToHaveCountAsync(0);
        }
        await Assertions.Expect(viewer.GetByRole(AriaRole.Link, new() { Name = "Accounts & rules" })).ToHaveCountAsync(0);
        var op = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(op, "operator");
        // Navigate inside the authenticated circuit to exercise AuthorizeRouteView.
        await op.EvaluateAsync("() => Blazor.navigateTo('/accounts')");
        await Assertions.Expect(op.GetByRole(AriaRole.Heading, new() { Name = "Access denied" })).ToBeVisibleAsync();
        await Assertions.Expect(op.GetByRole(AriaRole.Button, new() { Name = "Disable", Exact = true })).ToHaveCountAsync(0);
    }, output);

    [Fact]
    public Task AdministratorRunsDetectedScenarioThenAcknowledgesAndCommentsOnItsIncident() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        var diagnostics = new BrowserDiagnostics();
        await diagnostics.WatchAsync(page);
        await BrowserTestSession.LoginAsync(page, "administrator");
        await BrowserTestSession.NavigateAsync(page, "Scenarios");
        await Row(page, "Privilege escalation attempt").GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(page.Locator("section").First).ToContainTextAsync("Completed · Detected", new() { Timeout = 15_000 });
        var run = session.Factory.Services.GetRequiredService<Security.Scenarios.ScenarioEngine>().Current!;
        Assert.Equal(Application.Scenarios.ScenarioOutcome.Detected, run.Outcome);
        await BrowserTestSession.NavigateAsync(page, "Incidents");
        var incidents = await session.Factory.Services.GetRequiredService<Application.Incidents.IncidentService>()
            .QueryAsync(new(), CancellationToken.None);
        var incident = Assert.Single(incidents.Items, item => item.ScenarioRunId == run.RunId);
        await page.GetByRole(AriaRole.Link, new() { Name = incident.DisplayId, Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync(incident.DisplayId);
        await page.GetByLabel("New status").SelectOptionAsync("Acknowledged");
        await page.GetByRole(AriaRole.Button, new() { Name = "Change status", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(page.Locator("dt").Filter(new() { HasTextRegex = new Regex("^Status$") }).Locator("+ dd")).ToHaveTextAsync("Acknowledged");
        await page.GetByLabel("Comment", new() { Exact = true }).FillAsync("Browser exercise investigated and acknowledged.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add comment", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(page.Locator(".record-list")).ToContainTextAsync(["Browser exercise investigated and acknowledged."]);
        await diagnostics.AssertCleanAsync(page);
    }, output);

    [Fact]
    public Task SpoofedSensorShowsReportedValuesToOperatorAndGroundTruthToAdministrator() => BrowserTestSession.RunAsync(async session =>
    {
        var environment = session.Factory.Services.GetRequiredService<ITrainingEnvironment>();
        Assert.True(environment.SpoofSensor("LT-102", 31.25).Accepted);
        var op = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(op, "operator");
        await BrowserTestSession.NavigateAsync(op, "Sensors");
        await Assertions.Expect(Row(op, "LT-102").Locator(".reported-value")).ToHaveTextAsync("31.25 %");
        await Assertions.Expect(op.GetByRole(AriaRole.Columnheader, new() { Name = "True value" })).ToHaveCountAsync(0);
        await Assertions.Expect(op.Locator(".true-value")).ToHaveCountAsync(0);
        await Assertions.Expect(op.GetByRole(AriaRole.Columnheader, new() { Name = "Spoofing" })).ToHaveCountAsync(0);
        var admin = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(admin, "administrator");
        await BrowserTestSession.NavigateAsync(admin, "Sensors");
        await Assertions.Expect(Row(admin, "LT-102").Locator(".reported-value")).ToHaveTextAsync("31.25 %");
        await Assertions.Expect(Row(admin, "LT-102").Locator(".true-value")).Not.ToHaveTextAsync("31.25 %");
        await Assertions.Expect(Row(admin, "LT-102")).ToContainTextAsync("Active (31.25 %)");
    }, output);

    [Fact]
    public Task DisablingOperatorSignsOutItsAlreadyOpenHmiWithinTheRevalidationInterval() => BrowserTestSession.RunAsync(async session =>
    {
        var op = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(op, "operator");
        await BrowserTestSession.NavigateAsync(op, "Pumps");
        await Assertions.Expect(Row(op, "P-101").GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true })).ToBeVisibleAsync();
        var admin = await session.NewPageAsync();
        await BrowserTestSession.LoginAsync(admin, "administrator");
        await BrowserTestSession.NavigateAsync(admin, "Accounts & rules");
        await Row(admin, "operator").GetByRole(AriaRole.Button, new() { Name = "Disable", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(admin);
        await Assertions.Expect(Row(admin, "operator")).ToContainTextAsync("Disabled");
        // Leave the operator page completely idle: no request, reload or navigation triggers this sign-out.
        await Assertions.Expect(op.GetByLabel("Username")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(op).ToHaveURLAsync(new Regex("/account/login\\?"));
        await Assertions.Expect(op.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true })).ToHaveCountAsync(0);
        await op.GetByLabel("Username").FillAsync("operator");
        await op.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserTestSession.Password);
        await op.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Assertions.Expect(op.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Invalid username or password.");
    }, output);

    private static ILocator Row(IPage page, string text) =>
        page.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByText(text, new() { Exact = true }) });
}
