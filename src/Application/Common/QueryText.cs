namespace Application.Common;

public static class QueryText
{
    public const int MaxLength = 200;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }
}
