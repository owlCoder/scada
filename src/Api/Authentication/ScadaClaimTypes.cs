namespace Api.Authentication;

public static class ScadaClaimTypes
{
    /// <summary>Identifies the credential (API key fingerprint, password account or OIDC subject) for per-credential rate limits.</summary>
    public const string Credential = "scada:credential";

    /// <summary>How the caller signed in: <see cref="ScadaSignInSources"/>.</summary>
    public const string Source = "scada:source";
}
