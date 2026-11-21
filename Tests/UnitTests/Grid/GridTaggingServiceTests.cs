using Application.Events;
using Application.Grid;
using Application.Identity;
using Application.Realtime;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Microsoft.Extensions.Time.Testing;
using Security.Grid.Control;
using UnitTests.Grid.Support;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridTaggingServiceTests
{
    private static (GridTaggingService Service, GridEngineEventCollector Events, GridEngineRealtimeCollector Realtime) Create(IGridView? view = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 2, 2, 8, 0, 0, TimeSpan.Zero));
        var events = new GridEngineEventCollector();
        var realtime = new GridEngineRealtimeCollector();
        var service = new GridTaggingService(view ?? new GridEngine(new GridOptions(), time), events, realtime, time);
        return (service, events, realtime);
    }

    private static GridDefinition ManyLoads()
    {
        var bus = new GridBusDefinition("B", "Bus", 20, GridBusKind.Busbar, new(0, 0));
        var loads = Enumerable.Range(1, 250)
            .Select(i => new GridLoadDefinition($"L{i}", $"Load {i}", "B", 0.1, 0.95, GridLoadProfile.Residential, 10, false, new(i, 0)))
            .ToArray();
        return new GridDefinition("Many loads", 50, [bus], [new("S", "Source", "B", 1, 1e9, new(0, 0))], [], [], [], loads, [], [], [], []);
    }

    [Fact]
    public async Task PlaceAndRemoveRequireOperatorRoleAndAreAudited()
    {
        var (service, events, _) = Create();
        var rejected = await service.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "work", DispatcherGridRig.Viewer, CancellationToken.None);
        Assert.Equal(GridCommandRejection.NotAuthorized, rejected.Reason);
        Assert.False(rejected.Accepted);
        var removed = await service.RemoveTagAsync("whatever", DispatcherGridRig.Viewer, CancellationToken.None);
        Assert.Equal(GridCommandRejection.NotAuthorized, removed.Reason);
        Assert.All(events.Items, e =>
        {
            Assert.Equal(EventCategories.Audit, e.Category);
            Assert.Equal("grid:tags", e.Source);
        });
        Assert.Equal(2, events.Items.Count);
    }

    [Fact]
    public async Task PlaceTagValidatesElementReasonKindAndDuplicates()
    {
        var (service, _, _) = Create();
        Assert.Equal(GridCommandRejection.UnknownTarget,
            (await service.PlaceTagAsync("NOPE", GridTagKind.DoNotOperate, "r", DispatcherGridRig.Operator, CancellationToken.None)).Reason);
        Assert.Equal(GridCommandRejection.InvalidValue,
            (await service.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "", DispatcherGridRig.Operator, CancellationToken.None)).Reason);
        Assert.Equal(GridCommandRejection.InvalidValue,
            (await service.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, new string('x', 201), DispatcherGridRig.Operator, CancellationToken.None)).Reason);
        Assert.Equal(GridCommandRejection.InvalidValue,
            (await service.PlaceTagAsync("QF1", (GridTagKind)77, "r", DispatcherGridRig.Operator, CancellationToken.None)).Reason);
        Assert.True((await service.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "r", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Equal(GridCommandRejection.AlreadyInState,
            (await service.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "again", DispatcherGridRig.Operator, CancellationToken.None)).Reason);
        // A different kind on the same element is allowed.
        Assert.True((await service.PlaceTagAsync("QF1", GridTagKind.Information, "note", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Equal(2, service.Tags.Count);
    }

    [Fact]
    public async Task PlaceTagAcceptsExactly200CharsReasonAndGeneratesUniqueIds()
    {
        var (service, _, _) = Create();
        var reason = new string('x', 200);
        var first = await service.PlaceTagAsync("QF1", GridTagKind.Earthed, reason, DispatcherGridRig.Operator, CancellationToken.None);
        var second = await service.PlaceTagAsync("Q0", GridTagKind.WorkInProgress, reason, DispatcherGridRig.Operator, CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        var tags = service.Tags;
        Assert.Equal(2, tags.Count);
        Assert.NotEqual(tags[0].Id, tags[1].Id);
        Assert.Equal(reason, tags[0].Reason);
        Assert.Equal(DispatcherGridRig.Operator.Name, tags[0].PlacedBy);
    }

    [Fact]
    public async Task PlaceAndRemovePublishGridTagsAndAuditEveryChange()
    {
        var (service, events, realtime) = Create();
        var placed = await service.PlaceTagAsync("F1-1", GridTagKind.DoNotOperate, "cable work", DispatcherGridRig.Operator, CancellationToken.None);
        Assert.True(placed.Accepted);
        var tag = Assert.Single(service.Tags);
        var published = realtime.Snapshot().Where(m => m.Topic == RealtimeTopics.GridTags).ToArray();
        var payload = Assert.IsType<GridTag[]>(Assert.Single(published).Payload);
        Assert.Equal(tag.Id, Assert.Single(payload).Id);

        var removed = await service.RemoveTagAsync(tag.Id, DispatcherGridRig.Operator, CancellationToken.None);
        Assert.True(removed.Accepted);
        Assert.Empty(service.Tags);
        Assert.Empty(Assert.IsType<GridTag[]>(realtime.Snapshot().Last(m => m.Topic == RealtimeTopics.GridTags).Payload));
        Assert.Equal(GridCommandRejection.UnknownTarget,
            (await service.RemoveTagAsync(tag.Id, DispatcherGridRig.Operator, CancellationToken.None)).Reason);

        Assert.Equal(3, events.Items.Count);
        Assert.All(events.Items, e => Assert.Equal(EventCategories.Audit, e.Category));
        Assert.Contains(events.Items, e => e.Message.Contains("placed", StringComparison.OrdinalIgnoreCase) && e.Message.Contains(tag.Id, StringComparison.Ordinal));
        Assert.Contains(events.Items, e => e.Message.Contains("removed", StringComparison.OrdinalIgnoreCase) && e.Message.Contains(tag.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TagLimitIs200()
    {
        var (service, _, _) = Create(new DispatcherFakeGridView(ManyLoads()));
        for (var i = 1; i <= 200; i++)
            Assert.True((await service.PlaceTagAsync($"L{i}", GridTagKind.Information, $"note {i}", DispatcherGridRig.Operator, CancellationToken.None)).Accepted,
                $"tag {i} should be accepted");
        var overflow = await service.PlaceTagAsync("L201", GridTagKind.Information, "one too many", DispatcherGridRig.Operator, CancellationToken.None);
        Assert.False(overflow.Accepted);
        Assert.Equal(GridCommandRejection.InvalidValue, overflow.Reason);
        Assert.Equal(200, service.Tags.Count);
    }

    [Fact]
    public async Task TagsAreClearedWhenADifferentDefinitionIsActivated()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "hold", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Single(rig.Tagging.Tags);

        var saved = await rig.Store.SaveAsync(DefaultGridDefinition.Create() with { Name = "Activated v2" }, "instructor", "activation test", CancellationToken.None);
        var activated = await rig.Coordinator.ActivateVersionAsync(saved.Version, DispatcherGridRig.Instructor, CancellationToken.None);
        Assert.True(activated.Accepted);

        Assert.Empty(rig.Tagging.Tags);
        await rig.StepAsync();
        var lastTags = rig.Realtime.Snapshot().Last(m => m.Topic == RealtimeTopics.GridTags).Payload;
        Assert.Empty(Assert.IsType<GridTag[]>(lastTags));
    }

    [Fact]
    public async Task BlockingKindsAreExactlyDoNotOperateEarthedAndWorkInProgress()
    {
        Assert.True(GridTaggingService.BlocksOperations(GridTagKind.DoNotOperate));
        Assert.True(GridTaggingService.BlocksOperations(GridTagKind.Earthed));
        Assert.True(GridTaggingService.BlocksOperations(GridTagKind.WorkInProgress));
        Assert.False(GridTaggingService.BlocksOperations(GridTagKind.Information));
    }
}
