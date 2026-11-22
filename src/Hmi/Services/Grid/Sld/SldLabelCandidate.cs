namespace Hmi.Services.Grid.Sld;

/// <summary>A value/label box to place: centre in SVG units plus the box size.</summary>
public sealed record SldLabelCandidate(string Id, double CenterX, double CenterY, double Width, double Height);
