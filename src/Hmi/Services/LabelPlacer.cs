namespace Hmi.Services;

/// <summary>
/// Generic greedy collision-free label placement shared by the grid SLD and the water P&amp;ID. Each candidate first
/// tries its own preferred centre, then offset rings (8 compass directions at <see cref="RingStep"/> px steps) out to
/// <paramref name="maxDistance"/>; the first slot whose rectangle intersects neither an obstacle (symbol/busbar/
/// marker boxes) nor an already-placed box wins. Candidates are placed in priority order (highest first, original
/// order preserved within a priority); a candidate with no free slot inside the search radius is DROPPED instead of
/// overlapping, so the result never contains two intersecting boxes. <paramref name="unitScale"/> converts the fixed
/// slot distances to diagram units (pass 1 / pixels-per-unit so boxes keep a constant on-screen size at any zoom).
/// </summary>
public static class LabelPlacer
{
    /// <summary>Clear space kept around every box (px).</summary>
    public const double Margin = 6;

    /// <summary>Distance between offset rings (px).</summary>
    public const double RingStep = 13;

    /// <summary>A box placed farther than this from its preferred centre counts as displaced (leader line, px).</summary>
    public const double DisplaceAfter = 39;

    /// <summary>Fallback search radius (px) when the caller does not bound the search.</summary>
    public const double DefaultMaxDistance = 4000;

    private static readonly (double Dx, double Dy)[] Directions =
    [
        (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1)
    ];

    public static IReadOnlyList<LabelPlacement> Place(
        IReadOnlyList<LabelBoxCandidate> candidates,
        IReadOnlyList<LabelObstacle> obstacles,
        double unitScale = 1,
        double maxDistance = DefaultMaxDistance)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var scale = Math.Max(unitScale, 1e-6);
        var maxDist = Math.Max(maxDistance, 0) * scale;
        var placed = new List<(double X, double Y, double W, double H)>();
        var result = new List<LabelPlacement>();
        var ordered = candidates
            .Select((candidate, index) => (Candidate: candidate, Index: index))
            .OrderByDescending(t => t.Candidate.Priority)
            .ThenBy(t => t.Index);
        foreach (var (candidate, _) in ordered)
        {
            var slot = FindSlot(candidate, obstacles, placed, scale, maxDist);
            if (slot is null)
                continue; // no free slot within the search radius: drop this (least important) label
            placed.Add((slot.Value.X, slot.Value.Y, candidate.Width, candidate.Height));
            result.Add(new LabelPlacement(candidate.Id, slot.Value.X, slot.Value.Y, slot.Value.Displaced));
        }
        return result;
    }

    private static (double X, double Y, bool Displaced)? FindSlot(
        LabelBoxCandidate candidate,
        IReadOnlyList<LabelObstacle> obstacles,
        List<(double X, double Y, double W, double H)> placed,
        double scale,
        double maxDist)
    {
        // The preferred slot itself first (offsets already keep it clear of its own symbol).
        if (Free(candidate.CenterX - candidate.Width / 2, candidate.CenterY - candidate.Height / 2,
                candidate.Width, candidate.Height, obstacles, placed))
            return (candidate.CenterX - candidate.Width / 2, candidate.CenterY - candidate.Height / 2, false);
        // Then the 8 compass directions on offset rings out to the search radius.
        for (var distance = RingStep * scale; distance <= maxDist + 1e-9; distance += RingStep * scale)
            foreach (var (dx, dy) in Directions)
            {
                var x = candidate.CenterX + dx * distance - candidate.Width / 2;
                var y = candidate.CenterY + dy * distance - candidate.Height / 2;
                if (Free(x, y, candidate.Width, candidate.Height, obstacles, placed))
                    return (x, y, distance > DisplaceAfter * scale);
            }
        return null;
    }

    private static bool Free(
        double x, double y, double w, double h,
        IReadOnlyList<LabelObstacle> obstacles,
        List<(double X, double Y, double W, double H)> placed) =>
        !obstacles.Any(o => Intersects(x, y, w, h, o.X, o.Y, o.Width, o.Height))
        && !placed.Any(p => Intersects(x, y, w, h, p.X, p.Y, p.W, p.H));

    private static bool Intersects(double x, double y, double w, double h, double ox, double oy, double ow, double oh) =>
        x - Margin < ox + ow && x + w + Margin > ox && y - Margin < oy + oh && y + h + Margin > oy;
}
