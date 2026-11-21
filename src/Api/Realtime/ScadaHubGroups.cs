namespace Api.Realtime;

public static class ScadaHubGroups
{
    /// <summary>Administrators: receive ground-truth plant snapshots.</summary>
    public const string Instructors = "instructors";

    /// <summary>Viewers and operators: receive plant snapshots as the field instruments report them.</summary>
    public const string Operators = "operators";
}
