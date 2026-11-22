namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Greedy collision-free placement of measurement value boxes on the single-line diagram. Each candidate first tries
/// the slot around its element (8 compass directions on offset rings); a slot wins only when its rectangle intersects
/// neither an obstacle (the bounding boxes of ALL symbols, busbars, markers and id/name labels — see
/// <see cref="SldLabelLayout.Obstacles"/>) nor an already-placed box. Candidates are placed in priority order
/// (busbar kV &gt; feeder head A/% &gt; transformer % &gt; load MW &gt; others, see <see cref="SldLabelLayout.Priority"/>);
/// when no free slot exists within the LOD-appropriate search radius the least important label is DROPPED instead of
/// overlapping. <paramref name="unitScale"/> converts the fixed slot distances to SVG units (pass 1 / pixelsPerUnit
/// so boxes keep a constant on-screen size at any zoom); candidate sizes are already in SVG units.
/// </summary>
public static class SldLabelPlacer
{
    /// <summary>
    /// Places the candidates collision-free without obstacles (kept for simple callers/tests): the search spirals
    /// outward with the default radius, which always terminates because boxes are finite.
    /// </summary>
    public static IReadOnlyList<SldLabelPlacement> Place(IEnumerable<SldLabelCandidate> candidates, double unitScale = 1) =>
        Place(candidates, unitScale, [], LabelPlacer.DefaultMaxDistance);

    /// <summary>
    /// Places the candidates collision-free around obstacles. <paramref name="maxDistancePx"/> bounds the search in
    /// screen px (converted with <paramref name="unitScale"/>); candidates with no free slot inside the bound are
    /// dropped (returned without a placement).
    /// </summary>
    public static IReadOnlyList<SldLabelPlacement> Place(
        IEnumerable<SldLabelCandidate> candidates,
        double unitScale,
        IEnumerable<LabelObstacle> obstacles,
        double maxDistancePx) =>
        LabelPlacer.Place(
                candidates.Select(c => new LabelBoxCandidate(c.Id, c.CenterX, c.CenterY, c.Width, c.Height, c.Priority)).ToArray(),
                obstacles.ToArray(),
                unitScale,
                maxDistancePx)
            .Select(p => new SldLabelPlacement(p.Id, p.X, p.Y, p.Displaced))
            .ToArray();

    /// <summary>Value-box size for a text and optional quality flag (11 px tabular mono plus padding).</summary>
    public static (double Width, double Height) BoxSize(string? text, string? flag)
    {
        var width = 14 + (text?.Length ?? 0) * 7 + (flag is null ? 0 : 12);
        return (width, 18);
    }

    public static (double X, double Y) BoxTopLeft(SldLabelCandidate candidate, double centerX, double centerY) =>
        (centerX - candidate.Width / 2, centerY - candidate.Height / 2);
}
