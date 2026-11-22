namespace Hmi.Services.Grid.Sld;

/// <summary>One context-menu command.</summary>
public sealed record SldMenuItem(string Label, GridSldActionKind Action, bool Enabled = true);
