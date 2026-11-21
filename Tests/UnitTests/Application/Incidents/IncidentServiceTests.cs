using Application.Identity;
using Application.Incidents;
using Domain.Incidents;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Incidents;

public sealed class IncidentServiceTests
{
    private static readonly Actor Ana = new("ana", AccessRole.Operator);

    private static IncidentDraft Draft(TestRig rig, string key = "cyber:P-101", IncidentSeverity severity = IncidentSeverity.Medium) =>
        new("Unexpected pump start", "P-101 started without a command", IncidentCategory.Cybersecurity, severity, "detection-engine",
            key, ["P-101"], null, new IncidentEvidence(rig.Time.GetUtcNow(), "detection", Guid.NewGuid().ToString("N"), "evidence"));

    [Fact]
    public async Task EveryDeniedMutationIsAuditedWithoutChangingTheIncident()
    {
        var rig = new TestRig();
        var incident = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;
        var viewer = new Actor("guest", AccessRole.Viewer);
        var results = new[]
        {
            await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Acknowledged, null, null, viewer, CancellationToken.None),
            await rig.Incidents.AssignAsync(incident.Id, "guest", null, viewer, CancellationToken.None),
            await rig.Incidents.ChangePriorityAsync(incident.Id, IncidentPriority.Urgent, null, viewer, CancellationToken.None),
            await rig.Incidents.AddCommentAsync(incident.Id, "denied", viewer, CancellationToken.None)
        };
        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.Equal(4, rig.Buffer.GetRecent().Count(e => e.Category == global::Application.Events.EventCategories.AuthorizationDenied
            && e.Actor == "guest" && e.Message.Contains(incident.Id.ToString(), StringComparison.Ordinal)));
        Assert.Equal(incident, await rig.Incidents.GetAsync(incident.Id, CancellationToken.None));
        Assert.Empty(await rig.IncidentStore.GetCommentsAsync(incident.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RaisingCreatesANumberedIncidentAndMergesCorrelatedDetections()
    {
        var rig = new TestRig();

        var first = await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None);
        var second = await rig.Incidents.RaiseAsync(Draft(rig, severity: IncidentSeverity.Critical), CancellationToken.None);
        var other = await rig.Incidents.RaiseAsync(Draft(rig, "cyber:V-302"), CancellationToken.None);

        Assert.Equal("INC-00001", first.Incident!.DisplayId);
        Assert.Equal(first.Incident.Id, second.Incident!.Id);
        Assert.Equal(2, second.Incident.Evidence.Count);
        Assert.Equal(IncidentSeverity.Critical, second.Incident.Severity);
        Assert.Equal("INC-00002", other.Incident!.DisplayId);
        var page = await rig.Incidents.QueryAsync(new IncidentQuery(), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ResolvedIncidentIsNotReusedForNewDetections()
    {
        var rig = new TestRig();
        var first = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;
        await rig.Incidents.ChangeStatusAsync(first.Id, IncidentStatus.Acknowledged, null, null, Ana, CancellationToken.None);
        await rig.Incidents.ChangeStatusAsync(first.Id, IncidentStatus.Resolved, "False alarm", null, Ana, CancellationToken.None);

        var next = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;

        Assert.NotEqual(first.Id, next.Id);
    }

    [Fact]
    public async Task StaleVersionIsRejectedAsAConcurrencyConflict()
    {
        var rig = new TestRig();
        var incident = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;

        var ok = await rig.Incidents.AssignAsync(incident.Id, "ana", incident.Version, Ana, CancellationToken.None);
        var stale = await rig.Incidents.ChangePriorityAsync(incident.Id, IncidentPriority.Urgent, incident.Version, Ana, CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.Equal(incident.Version + 1, ok.Incident!.Version);
        Assert.Equal(IncidentOperationStatus.ConcurrencyConflict, stale.Status);
    }

    [Fact]
    public async Task ViewerCannotChangeIncidentsAndClosedIncidentsRejectComments()
    {
        var rig = new TestRig();
        var incident = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;

        var denied = await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Acknowledged, null, null,
            new Actor("guest", AccessRole.Viewer), CancellationToken.None);
        Assert.False(denied.Succeeded);

        await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Acknowledged, null, null, Ana, CancellationToken.None);
        await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Resolved, "done", null, Ana, CancellationToken.None);
        await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Closed, null, null, Ana, CancellationToken.None);
        var comment = await rig.Incidents.AddCommentAsync(incident.Id, "late note", Ana, CancellationToken.None);
        Assert.Equal(IncidentOperationStatus.InvalidTransition, comment.Status);
    }

    [Fact]
    public async Task CommentsAndTransitionsAppearOnTheTimeline()
    {
        var rig = new TestRig();
        var incident = (await rig.Incidents.RaiseAsync(Draft(rig), CancellationToken.None)).Incident!;
        await rig.Incidents.AddCommentAsync(incident.Id, "Investigating", Ana, CancellationToken.None);
        await rig.Incidents.ChangeStatusAsync(incident.Id, IncidentStatus.Acknowledged, null, null, Ana, CancellationToken.None);

        var details = await rig.Incidents.GetDetailsAsync(incident.Id, CancellationToken.None);

        Assert.Equal("Investigating", Assert.Single(details!.Comments).Text);
        Assert.Contains(details.Timeline, e => e.Kind == IncidentTimelineEntryKind.Created);
        Assert.Contains(details.Timeline, e => e.Kind == IncidentTimelineEntryKind.Commented);
        Assert.Contains(details.Timeline, e => e.ToStatus == IncidentStatus.Acknowledged);
        Assert.Contains(IncidentStatus.InProgress, details.AllowedTransitions);
    }

    [Fact]
    public async Task QueryFiltersSortsAndPages()
    {
        var rig = new TestRig();
        for (var i = 0; i < 5; i++)
            await rig.Incidents.RaiseAsync(Draft(rig, $"key-{i}", i % 2 == 0 ? IncidentSeverity.High : IncidentSeverity.Low), CancellationToken.None);

        var high = await rig.Incidents.QueryAsync(new IncidentQuery(Severity: IncidentSeverity.High), CancellationToken.None);
        var paged = await rig.Incidents.QueryAsync(new IncidentQuery(SortBy: IncidentSortField.Number, Descending: false, PageSize: 2, Page: 2),
            CancellationToken.None);
        var search = await rig.Incidents.QueryAsync(new IncidentQuery(Search: "INC-00004"), CancellationToken.None);

        Assert.Equal(3, high.TotalCount);
        Assert.Equal([3L, 4L], paged.Items.Select(i => i.Number));
        Assert.Equal(3, paged.TotalPages);
        Assert.Equal(4, Assert.Single(search.Items).Number);
    }
}
