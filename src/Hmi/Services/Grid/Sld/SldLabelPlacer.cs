namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Greedy collision-free placement of measurement value boxes. Each candidate tries a fixed ring of slots around its
/// element (8 compass directions, near and far); the first slot whose rectangle intersects no already-placed box wins.
/// If every slot is taken the search spirals outward, which always terminates because boxes are finite — so the
/// result never contains two overlapping boxes.
/// </summary>
public static class SldLabelPlacer
{
    private const double Margin = 6;
    private static readonly (double Dx, double Dy)[] Directions =
    [
        (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1)
    ];
    private const double Near = 26;
    private const double Far = 52;

    public static IReadOnlyList<SldLabelPlacement> Place(IEnumerable<SldLabelCandidate> candidates)
    {
        var placed = new List<(double X, double Y, double W, double H)>();
        var result = new List<SldLabelPlacement>();
        foreach (var candidate in candidates)
        {
            var slot = FindSlot(candidate, placed);
            placed.Add((slot.X, slot.Y, candidate.Width, candidate.Height));
            result.Add(new SldLabelPlacement(candidate.Id, slot.X, slot.Y));
        }
        return result;
    }

    /// <summary>Value-box size for a text and optional quality flag (11 px tabular mono plus padding).</summary>
    public static (double Width, double Height) BoxSize(string? text, string? flag)
    {
        var width = 14 + (text?.Length ?? 0) * 7 + (flag is null ? 0 : 12);
        return (width, 18);
    }

    public static (double X, double Y) BoxTopLeft(SldLabelCandidate candidate, double centerX, double centerY) =>
        (centerX - candidate.Width / 2, centerY - candidate.Height / 2);

    private static (double X, double Y) FindSlot(SldLabelCandidate candidate, List<(double X, double Y, double W, double H)> placed)
    {
        foreach (var distance in new[] { Near, Far })
            foreach (var (dx, dy) in Directions)
            {
                var centerX = candidate.CenterX + dx * distance;
                var centerY = candidate.CenterY + dy * distance;
                var (x, y) = BoxTopLeft(candidate, centerX, centerY);
                if (!Overlaps(x, y, candidate.Width, candidate.Height, placed))
                    return (x, y);
            }
        // Spiral outward until a free slot is found (always terminates: boxes are finite).
        for (var distance = Far + 26; distance <= 4000; distance += 26)
            foreach (var (dx, dy) in Directions)
            {
                var (x, y) = BoxTopLeft(candidate, candidate.CenterX + dx * distance, candidate.CenterY + dy * distance);
                if (!Overlaps(x, y, candidate.Width, candidate.Height, placed))
                    return (x, y);
            }
        // Unreachable in practice; keep the invariant by stacking below the element.
        var fallbackY = placed.Count == 0 ? candidate.CenterY : placed.Max(p => p.Y + p.H) + Margin;
        return (candidate.CenterX - candidate.Width / 2, fallbackY);
    }

    private static bool Overlaps(double x, double y, double w, double h, List<(double X, double Y, double W, double H)> placed) =>
        placed.Any(p => x - Margin < p.X + p.W && x + w + Margin > p.X && y - Margin < p.Y + p.H && y + h + Margin > p.Y);
}
