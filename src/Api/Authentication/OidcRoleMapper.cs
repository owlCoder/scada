using System.Security.Claims;
using Application.Identity;
using Security.Authentication;

namespace Api.Authentication;

public static class OidcRoleMapper
{
    /// <summary>Maps an identity-provider principal to a SCADA user with its highest mapped role, or null if none maps.</summary>
    public static AuthenticatedUser? Map(ClaimsPrincipal external, OidcSettings settings)
    {
        var roles = external.FindAll(settings.RoleClaimType).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        AccessRole? role = roles.Contains(settings.AdministratorRole) ? AccessRole.Administrator
            : roles.Contains(settings.OperatorRole) ? AccessRole.Operator
            : roles.Contains(settings.ViewerRole) ? AccessRole.Viewer
            : null;
        var subject = external.FindFirst("sub")?.Value;
        var name = external.FindFirst("preferred_username")?.Value ?? external.FindFirst("email")?.Value ?? subject;
        if (role is null || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(name))
            return null;
        return new AuthenticatedUser(name, role.Value, $"oidc:{subject}");
    }
}
