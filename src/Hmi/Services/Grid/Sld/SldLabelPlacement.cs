namespace Hmi.Services.Grid.Sld;

/// <summary>Placed label box: top-left corner in SVG units. <see cref="Displaced"/> marks a box pushed away from its element (draw a leader line).</summary>
public sealed record SldLabelPlacement(string Id, double X, double Y, bool Displaced = false);
