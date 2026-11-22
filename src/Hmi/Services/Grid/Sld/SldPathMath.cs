using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>Path arithmetic for wire polylines in SVG units.</summary>
public static class SldPathMath
{
    /// <summary>Point at the given fraction of the total path length plus the local direction in degrees.</summary>
    public static (DiagramPosition Point, double AngleDegrees) PointAt(IReadOnlyList<DiagramPosition> path, double fraction)
    {
        if (path.Count == 0)
            return (new DiagramPosition(0, 0), 0);
        if (path.Count == 1)
            return (path[0], 0);
        var total = Length(path);
        var target = Math.Clamp(fraction, 0, 1) * total;
        var walked = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var segment = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (segment < 1e-9)
                continue;
            if (walked + segment >= target)
            {
                var t = (target - walked) / segment;
                var point = new DiagramPosition(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                return (point, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            }
            walked += segment;
        }
        var last = path[^1];
        var previous = path[^2];
        return (last, Math.Atan2(last.Y - previous.Y, last.X - previous.X) * 180 / Math.PI);
    }

    public static double Length(IReadOnlyList<DiagramPosition> path)
    {
        var total = 0.0;
        for (var i = 1; i < path.Count; i++)
            total += Math.Sqrt((path[i].X - path[i - 1].X) * (path[i].X - path[i - 1].X) + (path[i].Y - path[i - 1].Y) * (path[i].Y - path[i - 1].Y));
        return total;
    }
}
