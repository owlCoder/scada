namespace Hmi.Services.Grid.Sld;

/// <summary>A context menu open at a screen position for one element.</summary>
public sealed record SldContextMenu(string ElementId, double ClientX, double ClientY, IReadOnlyList<SldMenuItem> Items);
