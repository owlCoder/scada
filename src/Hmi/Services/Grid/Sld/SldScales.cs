namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Counter-scales for one render pass of the diagram, derived from the viewport zoom: <see cref="Label"/> and
/// <see cref="Id"/> keep name/id labels legible (clamped on-screen size), <see cref="Value"/> keeps value boxes at a
/// fixed screen size, <see cref="Symbol"/> caps symbol size at the high-zoom level.
/// </summary>
public sealed record SldScales(double Label, double Id, double Value, double Symbol)
{
    public static SldScales Of(SldViewport viewport) => new(
        viewport.TextScale(12), viewport.TextScale(11), viewport.FixedScale, viewport.SymbolScale);
}
