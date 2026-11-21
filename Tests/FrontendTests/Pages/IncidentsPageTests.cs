using Application.Identity;
using Application.Incidents;
using Bunit;
using Domain.Incidents;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class IncidentsPageTests
{
    [Fact]
    public async Task ListLinksToDetailsAndAppliesFiltersSortingAndPaging()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        for (var index = 1; index <= 26; index++)
            await RaiseAsync(host, $"Level alert {index:D2}");
        var page = host.Context.Render<Incidents>();

        Assert.Equal(25, page.FindAll("tbody tr").Count);
        Assert.StartsWith("incidents/", page.Find("tbody a").GetAttribute("href"));
        page.FindAll(".pager button")[1].Click();
        Assert.Contains("Page 2 of 2", page.Find(".pager").TextContent);
        Assert.Single(page.FindAll("tbody tr"));

        page.FindAll("form select")[1].Change("High");
        page.FindAll("form select")[2].Change("Number");
        page.FindAll("form select")[3].Change("ascending");
        page.Find("input[type=search]").Change("Level alert 2");
        page.Find("form").Submit();

        Assert.Equal(7, page.FindAll("tbody tr").Count);
        Assert.Contains("Level alert 20", page.Find("tbody tr:first-child").TextContent);
        Assert.Contains("Page 1 of 1", page.Find(".pager").TextContent);
        Assert.Empty(page.FindAll(".dialog"));
    }

    [Fact]
    public async Task ViewerSeesEvidenceTimelineAndCommentsWithoutEditControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var incident = await RaiseAsync(host, "Level alert");
        await host.Rig.Incidents.AddCommentAsync(incident.Id, "Verified locally", incident.Version,
            new Actor("eva", AccessRole.Operator), CancellationToken.None);

        var detail = host.Context.Render<IncidentDetail>(parameters => parameters.Add(page => page.Id, incident.Id));

        Assert.Contains("Tank level crossed a threshold", detail.Markup);
        Assert.Contains("Incident created", detail.Markup);
        Assert.Contains("Comment added", detail.Markup);
        Assert.Contains("Verified locally", detail.Markup);
        Assert.Empty(detail.FindAll("button"));
    }

    [Fact]
    public async Task OperatorChangesStatusAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var incident = await RaiseAsync(host, "Level alert");
        var detail = host.Context.Render<IncidentDetail>(parameters => parameters.Add(page => page.Id, incident.Id));

        Assert.Equal(["", "Acknowledged"], detail.Find("select").QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        detail.Find("select").Change("Acknowledged");
        detail.FindAll("button").First(button => button.TextContent == "Change status").Click();
        Assert.Contains("Acknowledged", detail.Find(".dialog").TextContent);
        Assert.Equal(IncidentStatus.Open, (await host.Rig.Incidents.GetAsync(incident.Id, CancellationToken.None))!.Status);

        detail.Find(".dialog .btn-primary").Click();

        detail.WaitForAssertion(() => Assert.Contains("Status changed", detail.Find(".banner-ok").TextContent));
        Assert.Equal(IncidentStatus.Acknowledged, (await host.Rig.Incidents.GetAsync(incident.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task ConfirmingStaleCommentShowsVersionConflictWithoutPosting()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva");
        var incident = await RaiseAsync(host, "Level alert");
        var detail = host.Context.Render<IncidentDetail>(parameters => parameters.Add(page => page.Id, incident.Id));

        detail.Find("textarea").Input("Needs follow-up");
        detail.FindAll("button").First(button => button.TextContent == "Add comment").Click();
        Assert.Empty((await host.Rig.Incidents.GetDetailsAsync(incident.Id, CancellationToken.None))!.Comments);

        var assignment = await host.Rig.Incidents.AssignAsync(incident.Id, "ana", incident.Version,
            new Actor("ana", AccessRole.Operator), CancellationToken.None);
        Assert.True(assignment.Succeeded);

        detail.Find(".dialog .btn-primary").Click();
        detail.WaitForAssertion(() => Assert.Contains("Version conflict", detail.Find(".banner-error").TextContent));
        Assert.Empty((await host.Rig.Incidents.GetDetailsAsync(incident.Id, CancellationToken.None))!.Comments);
        detail.FindAll("button").First(button => button.TextContent == "Add comment").Click();
        detail.Find(".dialog .btn-primary").Click();

        detail.WaitForAssertion(() => Assert.Contains("Comment added", detail.Find(".banner-ok").TextContent));
        var updated = (await host.Rig.Incidents.GetDetailsAsync(incident.Id, CancellationToken.None))!;
        Assert.Equal("Needs follow-up", Assert.Single(updated.Comments).Text);
        Assert.Equal(assignment.Incident!.Version + 1, updated.Incident.Version);
    }

    private static async Task<Incident> RaiseAsync(HmiTestHost host, string title)
    {
        var result = await host.Rig.Incidents.RaiseAsync(new IncidentDraft(title, "Review tank condition", default,
            IncidentSeverity.High, "tests", title, ["T-101"], null,
            new IncidentEvidence(host.Rig.Time.GetUtcNow(), "alarm", "T-101", "Tank level crossed a threshold")), CancellationToken.None);
        return result.Incident!;
    }
}
