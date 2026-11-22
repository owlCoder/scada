namespace Hmi.Services.Shell;

/// <summary>
/// One navigable display of the shell: a rail icon and a quick-switcher row. <see cref="Icon"/> names an inline SVG.
/// <see cref="Short"/> is the compact rail label (at most 9 characters so it never truncates in the 56 px rail);
/// the full <see cref="Title"/> stays the tooltip and aria-label.
/// </summary>
public sealed record DisplayEntry(string Title, string Href, string Group, string Icon, bool AdminOnly = false, string? Short = null)
{
    public const string ProcessGroup = "Process";
    public const string OperationsGroup = "Operations";
    public const string SystemGroup = "System";
    public const string AdministrationGroup = "Administration";

    /// <summary>Short rail label; falls back to a truncated title when no explicit short label is set.</summary>
    public string ShortLabel => Short ?? (Title.Length <= 9 ? Title : Title[..9]);

    public static readonly string[] GroupOrder =
        [ProcessGroup, OperationsGroup, SystemGroup, AdministrationGroup];
}
