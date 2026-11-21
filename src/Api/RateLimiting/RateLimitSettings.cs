namespace Api.RateLimiting;

public sealed class RateLimitSettings
{
    public const string LoginPolicy = "login";

    /// <summary>Requests per minute for each signed-in user, or for each client IP when anonymous.</summary>
    public int PermitsPerMinute { get; set; } = 1200;

    /// <summary>Sign-in attempts per minute for each client IP.</summary>
    public int LoginPermitsPerMinute { get; set; } = 10;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PermitsPerMinute is < 1 or > 1_000_000)
            errors.Add("RateLimiting:PermitsPerMinute must be between 1 and 1000000.");
        if (LoginPermitsPerMinute is < 1 or > 10_000)
            errors.Add("RateLimiting:LoginPermitsPerMinute must be between 1 and 10000.");
        return errors;
    }
}
