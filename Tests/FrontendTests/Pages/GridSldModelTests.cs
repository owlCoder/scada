using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using FrontendTests.Support;
using Hmi.Services.Grid;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldModelTests
{
    private static SldStaticModel Model => SldModelBuilder.Build(new HmiFakeGridView().Definition);
    private static GridSnapshot Snapshot => new HmiFakeGridView().State;

    [Fact]
    public Task ModelContainsAllElementsWiresAndFrames()
    {
        var model = Model;
        Assert.Equal(12, model.Elements.Count);
        var line = model.Element("L1");
        Assert.NotNull(line);
        Assert.Equal(SldElementKind.Line, line!.Kind);
        var wire = Assert.Single(line.Wires);
        Assert.Contains(new DiagramPosition(80, 280), wire); // from B-MV
        Assert.Contains(new DiagramPosition(400, 280), wire); // to B-END
        Assert.Contains(new DiagramPosition(240, 280), wire); // waypoint
        var transformer = model.Element("T1");
        Assert.Equal(2, transformer!.Wires.Count); // HV and LV stubs
        var load = model.Element("LD1");
        Assert.Contains(new DiagramPosition(400, 360), Assert.Single(load!.Wires)); // through QL1
        var rtu = Assert.Single(model.Frames);
        Assert.Equal("RTU1", rtu.RtuId);
        Assert.Contains("Q1", rtu.ElementIds);
        Assert.True(model.Bounds.Width > 0 && model.Bounds.Height > 0);
        return Task.CompletedTask;
    }

    [Fact]
    public Task FrameIsTheBoundingBoxOfItsMemberAnchorsOnly()
    {
        var model = Model;
        var rtu = Assert.Single(model.Frames);
        // Members: B-MV (80,280), L1 (240,280), Q1 (160,280), T1 (80,160) in SVG units.
        var memberAnchors = rtu.ElementIds.Select(model.Element).Select(e => e!.Anchor).ToArray();
        Assert.All(memberAnchors, a => Assert.InRange(a.X, rtu.X, rtu.X + rtu.Width));
        Assert.All(memberAnchors, a => Assert.InRange(a.Y, rtu.Y, rtu.Y + rtu.Height));
        // The frame does not stretch over the wires that run to the shared feeder-end bus at x=400.
        Assert.True(rtu.X + rtu.Width < 400, $"frame right {rtu.X + rtu.Width} swallows the feeder end");
        Assert.True(rtu.Width < 300, $"frame width {rtu.Width} is padded over neighbours");
        return Task.CompletedTask;
    }

    [Fact]
    public Task StateBuilderKeyTextCarriesBusKvAndBranchLoadingAtLowZoom()
    {
        var definition = new HmiFakeGridView().Definition;
        var states = SldStateBuilder.Build(Snapshot, definition, [], null);
        Assert.Equal("20.20 kV", states["B-MV"].KeyText);       // busbar kV
        Assert.Equal("80.0 A · 20 %", states["L1"].KeyText);   // feeder head current + loading
        Assert.Equal("90.0 A · 42 %", states["T1"].KeyText);   // transformer loading
        Assert.Null(states["LD1"].KeyText);                    // load MW only in the full value box
        Assert.Null(states["Q1"].KeyText);                     // switch state is the symbol itself
        Assert.Equal("80.0 A · 20 %", states["L1"].ValueText);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchMatchesIdsAndNamesCaseInsensitively()
    {
        var index = new SldSearchIndex(Model);
        Assert.Equal(new[] { "B-END", "B-HV", "B-MV" }, index.Search("b-").Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "LD1" }, index.Search("TOWN").Select(e => e.Id).ToArray());
        Assert.Empty(index.Search(""));
        Assert.Empty(index.Search("   "));
        Assert.Empty(index.Search("zzz"));
        Assert.True(index.Search("e").Count <= 8); // capped
        return Task.CompletedTask;
    }

    [Fact]
    public Task MenuItemsDependOnKindAndRole()
    {
        var model = Model;
        var q1 = model.Element("Q1")!;
        var l1 = model.Element("L1")!;
        var operatorItems = SldMenuBuilder.For(q1, null, true, 0, 0).Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Open…", "Close…", "Place tag…", "Trend", "Details" }, operatorItems);
        var viewer = SldMenuBuilder.For(q1, null, false, 0, 0).Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Trend", "Details" }, viewer);
        var line = SldMenuBuilder.For(l1, null, true, 0, 0).Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Control window", "Trend", "Place tag…", "Details" }, line);
        var bus = SldMenuBuilder.For(model.Element("B-MV")!, null, true, 0, 0).Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Details", "Trend" }, bus);
        return Task.CompletedTask;
    }

    [Fact]
    public Task StateBuilderFlagsQualityAndAlarmsAndProtection()
    {
        var definition = new HmiFakeGridView().Definition;
        var state = Snapshot;
        var states = SldStateBuilder.Build(state, definition, [], "Q1");
        // V1 reports B-MV voltage with Uncertain quality -> flag U, not invalid.
        var bus = states["B-MV"];
        Assert.Equal("U", bus.QualityFlag);
        Assert.False(bus.Invalid);
        Assert.Equal("20.20 kV", bus.ValueText);
        Assert.True(bus.Selected == false);
        // AL1 is an active unacknowledged High alarm on B-MV.
        Assert.Equal(Domain.Process.Alarms.AlarmSeverity.High, bus.AlarmSeverity);
        Assert.True(bus.AlarmUnacknowledged);
        Assert.Equal("p2", bus.AlarmClass);
        // P1 tripped and protects Q1.
        Assert.True(states["Q1"].ProtectionTripped);
        Assert.True(states["Q1"].Selected);
        Assert.True(states["Q1"].Closed);
        Assert.False(states["QS1"].Closed);
        // Bad switch-state quality makes the switch invalid with "?".
        var bad = state with
        {
            Measurements = [.. state.Measurements,
            new GridMeasurementSnapshot("SW-QS1", "QS1", "RTU1", GridMeasurementKind.SwitchState, "", 0, MeasurementQuality.Bad, 1, false, null)]
        };
        var states2 = SldStateBuilder.Build(bad, definition, [], null);
        Assert.True(states2["QS1"].Invalid);
        Assert.Equal("B", states2["QS1"].QualityFlag);
        Assert.Equal("?", states2["QS1"].ValueText);
        Assert.Null(states2["QS1"].Closed);
        // Tags attach to their element only.
        var tagged = SldStateBuilder.Build(state, definition, [new GridTag("t", "L1", GridTagKind.DoNotOperate, "r", "eva", DateTimeOffset.UtcNow)], null);
        Assert.Single(tagged["L1"].Tags);
        Assert.Empty(tagged["Q1"].Tags);
        // Branch flow: L1 carries power from B-MV to B-END.
        Assert.True(states["L1"].HasFlow);
        Assert.True(states["L1"].FlowFrom);
        Assert.Equal("80.0 A · 20 %", states["L1"].ValueText);
        Assert.Equal("heat-ok", states["L1"].LoadingClass);
        return Task.CompletedTask;
    }

    [Fact]
    public Task PathMathWalksThePolyline()
    {
        var path = new DiagramPosition[] { new(0, 0), new(100, 0), new(100, 100) };
        var (mid, angle) = SldPathMath.PointAt(path, 0.75);
        Assert.Equal(new DiagramPosition(100, 50), mid);
        Assert.Equal(90, angle, 3);
        Assert.Equal(200, SldPathMath.Length(path), 6);
        var (start, _) = SldPathMath.PointAt(path, 0);
        Assert.Equal(new DiagramPosition(0, 0), start);
        return Task.CompletedTask;
    }
}
