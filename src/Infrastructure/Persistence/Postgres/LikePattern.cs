namespace Infrastructure.Persistence.Postgres;

/// <summary>
/// Builds LIKE patterns that treat the user's text literally. Npgsql sends <c>ESCAPE ''</c> unless an escape character is
/// given, so every query must pass <see cref="EscapeCharacter"/> to <c>EF.Functions.ILike</c>.
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = @"\";

    public static string Contains(string term) => "%" + Escape(term.Trim()) + "%";

    public static string Escape(string term) =>
        term.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
