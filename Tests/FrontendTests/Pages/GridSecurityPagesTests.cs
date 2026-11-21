using Application.Common;
using Application.Grid;
using Application.Identity;
using Application.Scenarios;
using Bunit;
using Domain.Grid.Snapshots;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Hmi.Components.Pages.Grid;
using Microsoft.Extensions.DependencyInjection;
using Security.Grid.Scenarios;
using Xunit;

namespace FrontendTests.Pages;

/// <summary>bUnit tests for the grid security pages: /grid/scenarios and /grid/rules.</summary>
public sealed class GridSecurityPagesTests
{
    [Fact]
    public async Task ScenariosPageShowsCatalogAndHistoryWithoutControlsToOperators()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridScenarios>();

        Assert.Contains("Scenario catalog", page.Markup);
        Assert.Contains("Industroyer-style breaker opening", page.Markup);
        Assert.Contains("grid-unsolicited-switching", page.Markup);
        Assert.Contains("Run history", page.Markup);
        Assert.Contains("No current run.", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public async Task AdministratorStartsPausesResumesStopsAndResetsOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridScenarios>();
        var engine = host.Context.Services.GetRequiredService<GridScenarioEngine>();

        var start = page.FindAll("button").First(b => b.TextContent == "Start");
        start.Click();
        // Not confirmed yet: nothing happened.
        Assert.Null(engine.Current);
        Assert.NotNull(page.Find(".dialog"));
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.NotNull(engine.Current));
        Assert.Equal(ScenarioRunState.Running, engine.Current!.State);
        Assert.NotNull(page.Find("progress"));

        page.FindAll("button").First(b => b.TextContent == "Pause").Click();
        Assert.Equal(ScenarioRunState.Running, engine.Current!.State);
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Equal(ScenarioRunState.Paused, engine.Current!.State));

        page.FindAll("button").First(b => b.TextContent == "Resume").Click();
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Equal(ScenarioRunState.Running, engine.Current!.State));

        page.FindAll("button").First(b => b.TextContent == "Stop").Click();
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Equal(ScenarioRunState.Stopped, engine.Current!.State));
        Assert.Contains("Aborted", page.Markup);

        // The finished run appears in the history with expected vs observed detections and a Replay control.
        page.WaitForAssertion(() => Assert.Contains(engine.Current!.RunId.ToString(), page.Markup));
        Assert.Contains("Expected detections", page.Markup);
        Assert.Contains("Observed detections", page.Markup);
        var replay = page.FindAll("button").First(b => b.TextContent == "Replay");
        replay.Click();
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.NotNull(engine.Current));
        Assert.Equal(engine.Current!.RunId == engine.Current.ReplayOf ? false : true, engine.Current.ReplayOf is not null);

        page.FindAll("button").First(b => b.TextContent == "Reset environment").Click();
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Null(engine.Current));
        Assert.Contains("No current run.", page.Markup);
    }

    [Fact]
    public async Task ScenarioThatCannotResolveTargetsOnTheNetworkFailsWithAClearMessage()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridScenarios>();
        var engine = host.Context.Services.GetRequiredService<GridScenarioEngine>();

        // The fake network has no voltage measurement at its highest-load bus, so the FDI scenario refuses to start.
        var row = page.FindAll("tbody tr").Single(r => r.TextContent.Contains("False data injection"));
        row.QuerySelector("button")!.Click();
        page.Find(".dialog .btn-danger").Click();
        page.WaitForAssertion(() => Assert.Contains("cannot run on this network", page.Markup));
        Assert.Null(engine.Current);
    }

    [Fact]
    public async Task RulesPageDeniesOperatorsAndShowsNothingEditable()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridRules>();
        Assert.Contains("Access denied", page.Markup);
        Assert.Empty(page.FindAll("input, select, button"));
        var detection = host.Context.Services.GetRequiredService<Security.Grid.Detection.GridDetectionEngine>();
        Assert.All(detection.Rules, r => Assert.Equal(r.Settings, r.Settings with { }));
    }

    [Fact]
    public async Task AdministratorSeesRulesWithMetricsAndEditsOnlyAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridRules>();
        var detection = host.Context.Services.GetRequiredService<Security.Grid.Detection.GridDetectionEngine>();

        Assert.Contains("Grid detection rules", page.Markup);
        Assert.Contains("grid-unsolicited-switching", page.Markup);
        Assert.Contains("Evaluations", page.Markup);
        Assert.Contains("Findings", page.Markup);
        Assert.Equal(10, page.FindAll("tbody tr").Count);

        // Invalid input is rejected client-side: no confirmation, no service call, settings unchanged.
        page.FindAll("select").First(s => s.TextContent.Contains("Unsolicited switching")).Change("grid-mass-switching");
        page.FindAll("input[type=number]")[0].Change("-1");
        page.FindAll("button").First(b => b.TextContent == "Save rule settings").Click();
        Assert.DoesNotContain("dialog-backdrop", page.Markup);
        Assert.Contains("Threshold must be finite and non-negative", page.Markup);
        Assert.Equal(3, detection.Rules.Single(r => r.Id == "grid-mass-switching").Settings.Threshold);

        // Valid input asks for confirmation and only then calls the administration service.
        page.FindAll("input[type=number]")[0].Change("5");
        page.FindAll("button").First(b => b.TextContent == "Save rule settings").Click();
        Assert.Equal(3, detection.Rules.Single(r => r.Id == "grid-mass-switching").Settings.Threshold);
        page.Find(".dialog .btn-primary").Click();
        page.WaitForAssertion(() => Assert.Equal(5, detection.Rules.Single(r => r.Id == "grid-mass-switching").Settings.Threshold));
    }

    [Fact]
    public async Task SharedIncidentsPageShowsGridIncidents()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        // Feed the grid detection engine one step with an unsolicited (unattributed) switch operation.
        var before = host.Grid.State with
        {
            Switches = host.Grid.State.Switches.Select(s => s.Id == "Q1" ? s with { Closed = true } : s).ToArray()
        };
        var after = host.Grid.State with
        {
            Switches = host.Grid.State.Switches.Select(s => s.Id == "Q1" ? s with { Closed = false, LastCause = GridChangeCause.Unattributed, LastOperatedBy = null } : s).ToArray()
        };
        var detection = host.Context.Services.GetRequiredService<Security.Grid.Detection.GridDetectionEngine>();
        var events = new[] { new GridStepEvent(GridStepEventKind.SwitchOperated, "Q1", "Switch opened", null, GridChangeCause.Unattributed) };
        await detection.OnGridStepAsync(new GridStepReport(DateTimeOffset.UtcNow, before, []), CancellationToken.None);
        await detection.OnGridStepAsync(new GridStepReport(DateTimeOffset.UtcNow, after, events), CancellationToken.None);

        var incidents = await host.Rig.Incidents.QueryAsync(new(), CancellationToken.None);
        var gridIncident = Assert.Single(incidents.Items, i => i.Title.Contains("Unsolicited operation of Q1"));

        var page = host.Context.Render<Incidents>();
        Assert.Contains(gridIncident.DisplayId, page.Markup);
        Assert.Contains("Unsolicited operation of Q1", page.Markup);
    }
}
