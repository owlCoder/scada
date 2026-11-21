using Api.Authentication;

namespace Api.RateLimiting;

public static class RateLimitPartitions
{
    /// <summary>Signed-in callers are limited per credential, anonymous callers per IP.</summary>
    public static string For(HttpContext context) =>
        context.User.FindFirst(ScadaClaimTypes.Credential)?.Value is { Length: > 0 } credential
            ? $"credential:{credential}"
            : ClientSource.Of(context);
}
