namespace Api.Authentication;

public static class ScadaClaimTypes
{
    public const string Mode = "scada:mode";

    /// <summary>Identifies the credential (API key fingerprint, password account or OIDC subject) for per-credential rate limits.</summary>
    public const string Credential = "scada:credential";

    /// <summary>How the caller signed in: <see cref="ScadaSignInSources"/>.</summary>
    public const string Source = "scada:source";

    /// <summary>Sign-in time (Unix seconds), so long-lived HMI circuits can detect a password change after sign-in.</summary>
    public const string Issued = "scada:issued";
}
