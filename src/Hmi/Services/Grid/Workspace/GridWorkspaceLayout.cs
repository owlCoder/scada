namespace Hmi.Services.Grid.Workspace;

/// <summary>Dock layout of the grid dispatcher workspace (collapsed flags, bottom dock height, active tab). Persisted per
/// user in localStorage by the workspace JS module; this record is the C#-owned state.</summary>
public sealed record GridWorkspaceLayout(bool RightCollapsed, bool BottomCollapsed, double? BottomHeightPx, GridWorkspaceTab Tab)
{
    public static GridWorkspaceLayout Default { get; } = new(false, false, null, GridWorkspaceTab.Alarms);

    public static string StorageKey(string user) => $"scada:grid-workspace:{user}";
}
