using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using Hmi.Services;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

/// <summary>
/// Live-review regression: value boxes must never intersect a symbol/busbar box or another label box — for the
/// default network and both templates, at fit zoom (~0.6-0.9 px/unit, low LOD with key values) and at 2.5x zoom
/// (high LOD with full value boxes). When no free slot exists the least important label is dropped instead.
/// </summary>
public sealed class GridSldLabelCollisionTests
{
    public static TheoryData<Func<GridDefinition>> Networks => new()
    {
        DefaultGridDefinition.Create,
        UrbanCableGridTemplate.Create,
        RuralOverheadGridTemplate.Create
    };

    [Theory]
    [MemberData(nameof(Networks))]
    public async Task NoLabelBoxIntersectsAnySymbolBoxOrOtherLabelBoxAtFitAndAt2Point5x(Func<GridDefinition> network)
    {
        var model = SldModelBuilder.Build(network());
        var states = States(model);
        var taps = model.Elements
            .Where(e => e.Kind == SldElementKind.Transformer)
            .ToDictionary(e => e.Id, e => (int?)3);

        // Fit on a ~1500x700 canvas (the dispatcher workspace area at 1920x1080).
        var fit = new SldViewport();
        fit.SetContainer(1500, 700);
        fit.Fit(model.Bounds);
        Assert.Equal(SldLod.Low, fit.Lod);
        await AssertPlacedClean(model, states, taps, fit);

        // 2.5x zoom (high LOD: full value boxes + tap boxes).
        var zoomed = new SldViewport();
        zoomed.SetContainer(1500, 700);
        zoomed.Fit(model.Bounds);
        while (zoomed.PixelsPerUnit < 2.5)
            zoomed.ZoomCenter(1.05);
        Assert.Equal(SldLod.High, zoomed.Lod);
        await AssertPlacedClean(model, states, taps, zoomed);
    }

    [Theory]
    [MemberData(nameof(Networks))]
    public Task KeyValuesOfBusesFeedersAndTransformersAreAllShownAtFit(Func<GridDefinition> network)
    {
        var model = SldModelBuilder.Build(network());
        var states = States(model);
        var taps = model.Elements
            .Where(e => e.Kind == SldElementKind.Transformer)
            .ToDictionary(e => e.Id, e => (int?)3);
        var fit = new SldViewport();
        fit.SetContainer(1500, 700);
        fit.Fit(model.Bounds);
        var candidates = SldLabelLayout.Candidates(model, states, fit.Lod, taps, fit.FixedScale);
        var placements = SldLabelLayout.Place(model, states, taps, fit.Lod, SldScales.Of(fit), fit.PixelsPerUnit);
        // Priority: busbar kV > feeder head A/% > transformer % — these must never be dropped at fit.
        var keyCandidates = candidates.Where(c => model.Element(c.Id)?.Kind is SldElementKind.Bus or SldElementKind.Line or SldElementKind.Transformer).ToArray();
        Assert.NotEmpty(keyCandidates);
        foreach (var candidate in keyCandidates)
            Assert.True(placements.ContainsKey(candidate.Id), $"key value {candidate.Id} was dropped at fit");
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(Networks))]
    public Task EverySymbolAndBusbarIsCoveredByAnObstacleBox(Func<GridDefinition> network)
    {
        var model = SldModelBuilder.Build(network());
        var states = States(model);
        var obstacles = SldLabelLayout.Obstacles(model, states, SldLod.Low, SldScales.Of(new SldViewport()));
        Assert.NotEmpty(obstacles);
        foreach (var element in model.Elements)
            Assert.Contains(obstacles, o =>
                element.Anchor.X >= o.X && element.Anchor.X <= o.X + o.Width
                && element.Anchor.Y >= o.Y && element.Anchor.Y <= o.Y + o.Height);
        return Task.CompletedTask;
    }

    [Fact]
    public Task DenseLabelsAreDisplacedOrDroppedByPriorityNeverOverlapping()
    {
        // 40 boxes competing for the slots around one occupied anchor, tight search radius:
        // some get displaced (flagged for the leader line), the rest are dropped — none overlap.
        var obstacles = new List<LabelObstacle> { LabelObstacle.Centered(100, 100, 40, 40) };
        var candidates = Enumerable.Range(0, 40)
            .Select(i => new SldLabelCandidate($"c{i}", 100, 100, 60, 18, i == 0 ? 100 : 0))
            .ToArray();
        var placements = SldLabelPlacer.Place(candidates, 1, obstacles, 120);
        Assert.NotEmpty(placements);
        Assert.True(placements.Count < candidates.Length, "with a tight radius the lowest-priority labels are dropped");
        Assert.Contains(placements, p => p.Id == "c0"); // the highest-priority label always survives
        Assert.Contains(placements, p => p.Displaced); // boxes beyond the adjacent ring are flagged for a leader line
        Assert.DoesNotContain(placements, p => p.Id == "c39"); // the last, lowest-priority label is among the dropped
        var boxes = placements.Select(p => (p.X, p.Y, W: 60.0, H: 18.0)).ToArray();
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
                Assert.False(Intersects(boxes[i], boxes[j]), $"boxes {i} and {j} overlap");
        foreach (var box in boxes)
            foreach (var obstacle in obstacles)
                Assert.False(Intersects(box, obstacle), "placed box intersects the symbol obstacle");
        return Task.CompletedTask;
    }

    private static Task AssertPlacedClean(SldStaticModel model, IReadOnlyDictionary<string, SldElementState> states,
        IReadOnlyDictionary<string, int?> taps, SldViewport viewport)
    {
        var candidates = SldLabelLayout.Candidates(model, states, viewport.Lod, taps, viewport.FixedScale);
        var obstacles = SldLabelLayout.Obstacles(model, states, viewport.Lod, SldScales.Of(viewport));
        var placements = SldLabelLayout.Place(model, states, taps, viewport.Lod, SldScales.Of(viewport), viewport.PixelsPerUnit);
        var boxes = placements.Join(candidates, p => p.Key, c => c.Id, (p, c) => (X: p.Value.X, Y: p.Value.Y, W: c.Width, H: c.Height)).ToArray();
        Assert.NotEmpty(boxes);
        foreach (var box in boxes)
            foreach (var obstacle in obstacles)
                Assert.False(Intersects(box, obstacle),
                    $"label {box.X:0.#},{box.Y:0.#} {box.W:0.#}x{box.H:0.#} intersects obstacle {obstacle.X:0.#},{obstacle.Y:0.#} {obstacle.Width:0.#}x{obstacle.Height:0.#} at ppu {viewport.PixelsPerUnit:0.##}");
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
                Assert.False(Intersects(boxes[i], boxes[j]),
                    $"labels {i} and {j} overlap at ppu {viewport.PixelsPerUnit:0.##}: {boxes[i]} vs {boxes[j]}");
        return Task.CompletedTask;
    }

    private static bool Intersects((double X, double Y, double W, double H) a, LabelObstacle o) =>
        a.X < o.X + o.Width && a.X + a.W > o.X && a.Y < o.Y + o.Height && a.Y + a.H > o.Y;

    private static bool Intersects((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;

    /// <summary>Plausible per-element state: buses show kV, branches A·%, loads/generators MW, switches their state.</summary>
    private static IReadOnlyDictionary<string, SldElementState> States(SldStaticModel model)
    {
        var dict = new Dictionary<string, SldElementState>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            var (value, key) = element.Kind switch
            {
                SldElementKind.Bus => ("20.20 kV", "20.20 kV"),
                SldElementKind.Line or SldElementKind.Transformer => ("268.3 A · 33 %", "268.3 A · 33 %"),
                SldElementKind.Load => ("0.38 MW", null),
                SldElementKind.Switch => ("closed", null),
                SldElementKind.Generator => ("0.30 MW", null),
                SldElementKind.Capacitor => ("0.12 Mvar · 2/4", null),
                _ => ((string?)null, null) // sources carry no value box
            };
            dict[element.Id] = new SldElementState(element.Id, true,
                element.Kind == SldElementKind.Switch ? true : null, false, null, value,
                element.Kind is SldElementKind.Line or SldElementKind.Transformer ? 33 : null,
                false, false, false, null, false, [], false, KeyText: key);
        }
        return dict;
    }
}
