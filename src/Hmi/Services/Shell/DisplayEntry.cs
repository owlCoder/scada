namespace Hmi.Services.Shell;

/// <summary>One navigable display of the shell: a rail icon and a quick-switcher row. <see cref="Icon"/> names an inline SVG.</summary>
public sealed record DisplayEntry(string Title, string Href, string Group, string Icon, bool AdminOnly = false)
{
    public const string ProcessGroup = "Process";
    public const string OperationsGroup = "Operations";
    public const string SystemGroup = "System";
    public const string AdministrationGroup = "Administration";

    public static readonly string[] GroupOrder =
        [ProcessGroup, OperationsGroup, SystemGroup, AdministrationGroup];
}
