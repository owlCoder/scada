namespace Hmi.Services.Grid.Sld;

/// <summary>A substation frame: a dashed rectangle grouping the elements one RTU measures/controls (SVG units).</summary>
public sealed record SldFrame(string RtuId, string Name, double X, double Y, double Width, double Height, IReadOnlyList<string> ElementIds);
