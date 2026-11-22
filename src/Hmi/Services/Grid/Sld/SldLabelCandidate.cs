namespace Hmi.Services.Grid.Sld;

/// <summary>A value/label box to place: centre in SVG units plus the box size. Higher <see cref="Priority"/> is placed first and survives when space runs out.</summary>
public sealed record SldLabelCandidate(string Id, double CenterX, double CenterY, double Width, double Height, int Priority = 0);
