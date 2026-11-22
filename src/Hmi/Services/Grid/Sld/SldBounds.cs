using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>Axis-aligned rectangle in SVG units.</summary>
public sealed record SldBounds(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static SldBounds Of(IEnumerable<DiagramPosition> points, double padding)
    {
        var list = points.ToArray();
        if (list.Length == 0)
            return new SldBounds(0, 0, 100, 100);
        var x = list.Min(p => p.X) - padding;
        var y = list.Min(p => p.Y) - padding;
        return new SldBounds(x, y, list.Max(p => p.X) - x + padding, list.Max(p => p.Y) - y + padding);
    }

    public SldBounds Pad(double padding) => new(X - padding, Y - padding, Width + 2 * padding, Height + 2 * padding);
}
