namespace Hmi.Services;

/// <summary>
/// An axis-aligned rectangle (top-left corner plus size, in diagram units) that placed label boxes must not
/// intersect — a symbol, a busbar, a marker or another label's reserved area.
/// </summary>
public sealed record LabelObstacle(double X, double Y, double Width, double Height)
{
    public static LabelObstacle Centered(double centerX, double centerY, double width, double height) =>
        new(centerX - width / 2, centerY - height / 2, width, height);
}
