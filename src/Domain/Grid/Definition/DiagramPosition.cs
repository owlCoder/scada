namespace Domain.Grid.Definition;

/// <summary>Position on the single-line diagram canvas in abstract grid units (the editor snaps to whole units).</summary>
public sealed record DiagramPosition(double X, double Y);
