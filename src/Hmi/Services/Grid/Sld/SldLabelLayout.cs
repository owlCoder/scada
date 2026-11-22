using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// The render-pass label layout of the single-line diagram: which value boxes exist at the current LOD, the obstacle
/// rectangles of every symbol/busbar/marker/name label, and the collision-free placement (via
/// <see cref="SldLabelPlacer"/>). Kept separate from the component so the placement is unit-testable for whole
/// network definitions at any zoom.
/// </summary>
public static class SldLabelLayout
{
    /// <summary>How far (screen px) a value box may travel from its element before it is dropped at the current LOD.</summary>
    public const double MaxSearchPx = 360;

    /// <summary>Extra clear space around symbol boxes (SVG units, symbol scale 1 — symbols only shrink with zoom).</summary>
    private const double SymbolPad = 6;

    /// <summary>
    /// Label importance: busbar kV &gt; feeder head A/% &gt; transformer % &gt; load MW &gt; everything else. Higher
    /// priority candidates are placed first, so they keep the slots next to their element when space runs out.
    /// </summary>
    public static int Priority(SldElementKind kind) => kind switch
    {
        SldElementKind.Bus => 40,
        SldElementKind.Line => 30,
        SldElementKind.Transformer => 20,
        SldElementKind.Load => 10,
        _ => 0
    };

    /// <summary>
    /// The value-box candidates of one render pass. Low/medium zoom places the compact key values (bus kV, branch
    /// A·%); high zoom places the full value box of every element plus the transformer tap box. Boxes are sized in
    /// screen px and converted with <paramref name="fixedScale"/> (= 1 / pixels per unit) so they keep a constant
    /// on-screen size.
    /// </summary>
    public static IReadOnlyList<SldLabelCandidate> Candidates(
        SldStaticModel model,
        IReadOnlyDictionary<string, SldElementState> states,
        SldLod lod,
        IReadOnlyDictionary<string, int?>? taps,
        double fixedScale)
    {
        var candidates = new List<SldLabelCandidate>();
        foreach (var element in model.Elements)
        {
            var state = states.GetValueOrDefault(element.Id);
            if (lod == SldLod.High)
            {
                if (state?.ValueText is { } value)
                    candidates.Add(Make(element, value, state.QualityFlag, fixedScale));
                if (element.Kind == SldElementKind.Transformer
                    && taps is not null
                    && taps.GetValueOrDefault(element.Id) is { } tap)
                {
                    var (width, height) = SldLabelPlacer.BoxSize(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"tap {tap}"), null);
                    candidates.Add(new SldLabelCandidate(element.Id + GridSldTapSuffix, element.Anchor.X, element.Anchor.Y,
                        width * fixedScale, height * fixedScale, Priority(element.Kind)));
                }
            }
            else if (state?.KeyText is { } key)
            {
                candidates.Add(Make(element, key, null, fixedScale));
            }
        }
        return candidates;
    }

    /// <summary>Candidate id suffix for the transformer tap box (kept next to the component's TapSuffix).</summary>
    public const string GridSldTapSuffix = "#tap";

    /// <summary>
    /// Obstacle rectangles for one render pass: the bounding box of every symbol and busbar (inflated), the alarm /
    /// protection / tag markers, the selection halo and the id/name label texts — so value boxes never cover a
    /// symbol or another label.
    /// </summary>
    public static IReadOnlyList<LabelObstacle> Obstacles(
        SldStaticModel model,
        IReadOnlyDictionary<string, SldElementState> states,
        SldLod lod,
        SldScales scales)
    {
        var obstacles = new List<LabelObstacle>();
        foreach (var element in model.Elements)
        {
            var (halfW, halfH) = SymbolHalfExtents(element.Kind);
            obstacles.Add(LabelObstacle.Centered(element.Anchor.X, element.Anchor.Y,
                2 * (halfW + SymbolPad), 2 * (halfH + SymbolPad)));
            // Alarm bell + protection flag sit above the symbol, tags on the left, the halo around a selected symbol.
            obstacles.Add(LabelObstacle.Centered(element.Anchor.X, element.Anchor.Y - 31, 64, 22));
            var state = states.GetValueOrDefault(element.Id);
            if (state is not null)
            {
                for (var i = 0; i < state.Tags.Count; i++)
                    obstacles.Add(new LabelObstacle(element.Anchor.X - 40, element.Anchor.Y + 18 + i * 20, 36, 18));
                if (state.Selected)
                    obstacles.Add(LabelObstacle.Centered(element.Anchor.X, element.Anchor.Y, 60, 60));
            }
            // The id (low LOD, buses and lines) or name (medium+) text next to the symbol.
            if (lod == SldLod.Low && (element.Kind == SldElementKind.Bus || element.Kind == SldElementKind.Line))
                obstacles.Add(TextBox(element.Anchor.X, element.Anchor.Y, 14, 14, 11, element.Id.Length, scales.Id));
            else if (lod >= SldLod.Medium)
                obstacles.Add(TextBox(element.Anchor.X, element.Anchor.Y, 16, 14, 12, element.Name.Length, scales.Label));
        }
        return obstacles;
    }

    /// <summary>
    /// The full render-pass placement: candidates + obstacles through <see cref="SldLabelPlacer"/>, keyed by candidate
    /// id. Dropped (lowest-priority, no free slot) candidates are simply absent.
    /// </summary>
    public static IReadOnlyDictionary<string, SldLabelPlacement> Place(
        SldStaticModel model,
        IReadOnlyDictionary<string, SldElementState> states,
        IReadOnlyDictionary<string, int?>? taps,
        SldLod lod,
        SldScales scales,
        double pixelsPerUnit)
    {
        var fixedScale = pixelsPerUnit <= 0 ? 1 : 1 / pixelsPerUnit;
        var candidates = Candidates(model, states, lod, taps, fixedScale);
        var obstacles = Obstacles(model, states, lod, scales);
        return SldLabelPlacer.Place(candidates, fixedScale, obstacles, MaxSearchPx).ToDictionary(p => p.Id);
    }

    private static SldLabelCandidate Make(SldElement element, string text, string? flag, double fixedScale)
    {
        var (width, height) = SldLabelPlacer.BoxSize(text, flag);
        return new SldLabelCandidate(element.Id, element.Anchor.X, element.Anchor.Y,
            width * fixedScale, height * fixedScale, Priority(element.Kind));
    }

    /// <summary>Approximate text bounding box of an id/name label drawn at (dx, dy) with the given counter-scale.</summary>
    private static LabelObstacle TextBox(double anchorX, double anchorY, double dx, double dy, int fontPx, int length, double scale) =>
        new(anchorX + dx * scale, anchorY - dy * scale - fontPx * scale,
            length * 7 * scale + 4, (fontPx + 4) * scale);

    /// <summary>Half extents of a symbol in SVG units at symbol scale 1 (symbols only shrink when zooming in).</summary>
    private static (double HalfW, double HalfH) SymbolHalfExtents(SldElementKind kind) => kind switch
    {
        SldElementKind.Bus => (34, 8),        // busbar line ±30 plus stroke
        SldElementKind.Line => (10, 10),      // no symbol: the wire midpoint
        SldElementKind.Transformer => (14, 22), // two circles r11 at cy ±8
        SldElementKind.Switch => (16, 18),    // breaker square / disconnector blade
        SldElementKind.Load => (14, 14),      // arrow triangle
        SldElementKind.Generator => (22, 22), // rect plus PV rays
        SldElementKind.Capacitor => (16, 21), // two plates ±13 x, ±18 y
        _ => (17, 17)                          // source circle r14
    };
}
