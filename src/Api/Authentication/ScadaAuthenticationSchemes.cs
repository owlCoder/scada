namespace Api.Authentication;

public static class ScadaAuthenticationSchemes
{
    /// <summary>Default policy scheme: forwards to <see cref="ApiKey"/> when an X-Api-Key header is present, else to <see cref="Cookie"/>.</summary>
    public const string Default = "Scada";
    public const string ApiKey = "ApiKey";
    public const string Cookie = "Cookies";
    public const string Oidc = "oidc";
    public const string ApiKeyHeader = "X-Api-Key";
}
