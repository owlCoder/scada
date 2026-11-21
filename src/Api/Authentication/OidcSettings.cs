namespace Api.Authentication;

/// <summary>
/// Optional OpenID Connect sign-in (for example Keycloak). Provider roles are mapped to SCADA roles; a user without any
/// mapped role cannot sign in. <see cref="MetadataAddress"/> may differ from <see cref="Authority"/> when the API reaches
/// the provider over an internal network.
/// </summary>
public sealed class OidcSettings
{
    public bool Enabled { get; set; }
    public string Authority { get; set; } = "";
    public string? MetadataAddress { get; set; }
    public string ClientId { get; set; } = "";
    public string? ClientSecret { get; set; }
    public bool RequireHttpsMetadata { get; set; } = true;
    public string RoleClaimType { get; set; } = "roles";
    public string ViewerRole { get; set; } = "scada-viewer";
    public string OperatorRole { get; set; } = "scada-operator";
    public string AdministratorRole { get; set; } = "scada-administrator";
    public string DisplayName { get; set; } = "single sign-on";

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled)
            return [];
        var errors = new List<string>();
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out _))
            errors.Add("Authentication:Oidc:Authority must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ClientId))
            errors.Add("Authentication:Oidc:ClientId is required.");
        if (MetadataAddress is { Length: > 0 } metadata && !Uri.TryCreate(metadata, UriKind.Absolute, out _))
            errors.Add("Authentication:Oidc:MetadataAddress must be an absolute URL.");
        return errors;
    }
}
