namespace Hmi.Services;

/// <summary>
/// A placed label box: top-left corner in diagram units. <see cref="Displaced"/> marks a box that had to leave the
/// ring directly around its element, so the renderer can draw a thin leader line back to it.
/// </summary>
public sealed record LabelPlacement(string Id, double X, double Y, bool Displaced = false);
