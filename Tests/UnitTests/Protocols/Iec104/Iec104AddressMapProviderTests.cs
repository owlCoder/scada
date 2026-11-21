using Application.Events;
using Microsoft.Extensions.Time.Testing;
using Protocols.Iec104;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class Iec104AddressMapProviderTests
{
    private static (Iec104FakeGridView View, GridEngineEventCollector Events, Iec104AddressMapProvider Maps) Create()
    {
        var view = new Iec104FakeGridView();
        var events = new GridEngineEventCollector();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero));
        return (view, events, new Iec104AddressMapProvider(view, events, time));
    }

    [Fact]
    public Task BuildsTheInitialMapFromTheActiveDefinition()
    {
        var (view, _, maps) = Create();
        var map = maps.Current;
        Assert.Equal("Sample grid", map.DefinitionName);
        Assert.Equal(2, map.Rtus.Count);
        Assert.NotNull(map.Rtu(Iec104SampleGrid.Rtu1CommonAddress));
        Assert.NotNull(map.Rtu(Iec104SampleGrid.Rtu2CommonAddress));
        Assert.Same(map, maps.Current);
        Assert.Same(view.Definition, view.Definition);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SyncWithTheSameDefinitionKeepsTheMapAndStaysSilent()
    {
        var (view, events, maps) = Create();
        var before = maps.Current;
        Assert.Same(before, maps.Sync(view.Definition));
        Assert.Empty(events.Items);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SyncWithANewDefinitionInstanceRebuildsTheMapAndAuditsIt()
    {
        var (view, events, maps) = Create();
        var before = maps.Current;
        var v2 = Iec104SampleGrid.CreateDefinition() with { Name = "Sample grid v2" };
        view.SetDefinition(v2);
        var rebuilt = maps.Sync(view.Definition);
        Assert.NotSame(before, rebuilt);
        Assert.Same(rebuilt, maps.Current);
        Assert.Equal("Sample grid v2", rebuilt.DefinitionName);
        Assert.Equal(before.Rtus.Count, rebuilt.Rtus.Count);
        var audit = Assert.Single(events.Items);
        Assert.Equal(EventCategories.Protocol, audit.Category);
        Assert.Equal(EventSeverity.High, audit.Severity);
        Assert.Contains("rebuilt", audit.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sample grid v2", audit.Message, StringComparison.Ordinal);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RebuildProducesTheMapOfTheNewDefinition()
    {
        var (_, _, maps) = Create();
        var v2 = Iec104SampleGrid.CreateDefinition() with { Name = "Sample grid v2" };
        var rtu2 = v2.Rtus.Single(r => r.Id == Iec104SampleGrid.Rtu2Id);
        var withoutTransformer = rtu2 with { ElementIds = [.. rtu2.ElementIds.Where(e => e != Iec104SampleGrid.TransformerId)] };
        var changed = v2 with { Rtus = [.. v2.Rtus.Select(r => r.Id == rtu2.Id ? withoutTransformer : r)] };
        var rebuilt = maps.Sync(changed);
        // The tap point of the transformer is gone from CA 2.
        Assert.Null(rebuilt.Point(Iec104SampleGrid.Rtu2CommonAddress, Iec104AddressMap.TapIoaBase));
        Assert.NotNull(rebuilt.Point(Iec104SampleGrid.Rtu1CommonAddress, Iec104AddressMap.SwitchIoaBase));
        return Task.CompletedTask;
    }
}
