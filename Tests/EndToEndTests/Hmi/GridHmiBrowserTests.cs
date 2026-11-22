using System.Text.RegularExpressions;
using EndToEndTests.Support;
using Microsoft.Playwright;
using Xunit;

namespace EndToEndTests.Hmi;

/// <summary>Grid-mode end-to-end: sign in choosing the grid, overview totals, live diagram, select-before-operate on a
/// feeder breaker (with confirmation) causing lost customers and an alarm, then closing it again; editor gating by role.</summary>
public sealed class GridHmiBrowserTests(ITestOutputHelper output)
{
    [Fact]
    public Task GridModeSignInOverviewDiagramSboBreakerOperationAndEditorGating() => BrowserTestSession.RunAsync(async session =>
    {
        var page = await session.NewPageAsync();
        await SignInGridAsync(page, "operator");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/grid$"));
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Electrical distribution grid");

        // Overview shows the totals, including import/losses computed from reported measurements (not zero).
        var metrics = page.Locator(".metric strong");
        await Assertions.Expect(metrics.Nth(0)).ToContainTextAsync("MW");
        var import = await metrics.Nth(2).InnerTextAsync();
        var losses = await metrics.Nth(3).InnerTextAsync();
        Assert.DoesNotContain("0.00 MW", import);
        Assert.Contains("MW", losses);
        var customers = await metrics.Nth(4).InnerTextAsync();
        var suppliedBefore = int.Parse(Regex.Match(customers, @"^(\d+)").Groups[1].Value);

        // The single-line diagram page is the dispatcher workspace: canvas, control window dock and bottom dock.
        await BrowserTestSession.NavigateAsync(page, "Single-line diagram");
        await Assertions.Expect(page.Locator(".grid-workspace")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".workspace-canvas .grid-sld")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".dock-right")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".dock-bottom .dock-tabs")).ToBeVisibleAsync();

        // Open feeder breaker QF1 via select-before-operate, each step confirmed.
        await BrowserTestSession.NavigateAsync(page, "Switches");
        await Row(page, "QF1").GetByRole(AriaRole.Button, new() { Name = "Open QF1", Exact = true }).ClickAsync();
        var faceplate = page.Locator(".control-window");
        await Assertions.Expect(faceplate).ToBeVisibleAsync();
        await faceplate.GetByRole(AriaRole.Button, new() { Name = "Select open", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(faceplate).ToContainTextAsync("Open");
        await faceplate.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(faceplate.Locator("dl.facts")).ToContainTextAsync("Open");

        // Customers supplied drop and a loss-of-supply alarm appears.
        await NavigateGridOverviewAsync(page);
        var customersAfter = await page.Locator(".metric strong").Nth(4).InnerTextAsync();
        var suppliedAfter = int.Parse(Regex.Match(customersAfter, @"^(\d+)").Groups[1].Value);
        Assert.True(suppliedAfter < suppliedBefore, $"customers {suppliedBefore} -> {suppliedAfter}");
        await NavigateGridAlarmsAsync(page);
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Load has lost supply");

        // Close it again through SBO; supply and alarms recover.
        await BrowserTestSession.NavigateAsync(page, "Switches");
        await Row(page, "QF1").GetByRole(AriaRole.Button, new() { Name = "Open QF1", Exact = true }).ClickAsync();
        await faceplate.GetByRole(AriaRole.Button, new() { Name = "Select close", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await faceplate.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();
        await BrowserTestSession.ConfirmAsync(page);
        await Assertions.Expect(faceplate.Locator("dl.facts")).ToContainTextAsync("Closed");
        await NavigateGridOverviewAsync(page);
        await Assertions.Expect(page.Locator(".metric strong").Nth(4)).ToHaveTextAsync(customers);
        await NavigateGridAlarmsAsync(page);
        // The loss-of-supply alarms returned to normal (still listed as returned) and the top bar counts no active alarm.
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Returned to normal");
        await Assertions.Expect(page.Locator(".topbar").GetByText("active alarm(s)")).ToHaveTextAsync("0 active alarm(s)");

        // The network editor loads for an administrator and denies an operator.
        var admin = await session.NewPageAsync();
        await SignInGridAsync(admin, "administrator");
        await BrowserTestSession.NavigateAsync(admin, "Network editor");
        await Assertions.Expect(admin.Locator("h1")).ToHaveTextAsync("Network editor");

        var op = await session.NewPageAsync();
        await SignInGridAsync(op, "operator");
        await op.EvaluateAsync("() => Blazor.navigateTo('/grid/editor')");
        await Assertions.Expect(op.GetByRole(AriaRole.Heading, new() { Name = "Access denied" })).ToBeVisibleAsync();
        await Assertions.Expect(op.Locator("svg")).ToHaveCountAsync(0);
    }, output);

    private static async Task NavigateGridAlarmsAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Alarms", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Grid alarms");
    }

    private static async Task NavigateGridOverviewAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Grid overview", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Electrical distribution grid");
    }

    private static async Task SignInGridAsync(IPage page, string username)
    {
        await page.GotoAsync("/account/login");
        await page.Locator("input[name=mode][value=Grid]").CheckAsync();
        await page.GetByLabel("Username").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserTestSession.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".topbar")).ToContainTextAsync($"{username} ·");
    }

    private static ILocator Row(IPage page, string text) =>
        page.GetByRole(AriaRole.Row).Filter(new() { HasText = text });
}
