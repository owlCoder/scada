namespace Hmi.Services.Grid.Sld;

/// <summary>A pointer position reported by an element: used for the context menu and the hover tooltip.</summary>
public sealed record SldPointerRequest(string ElementId, double ClientX, double ClientY);
