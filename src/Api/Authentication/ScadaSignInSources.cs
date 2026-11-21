namespace Api.Authentication;

public static class ScadaSignInSources
{
    public const string ApiKey = "api-key";
    public const string Password = "password";
    public const string Oidc = "oidc";

    /// <summary>Sources that use the session cookie and therefore need antiforgery protection on state-changing requests.</summary>
    public static bool UsesCookie(string? source) => source is Password or Oidc;
}
