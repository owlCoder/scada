using System.Security.Claims;
using Security.Authentication;

namespace Api.Authentication;

public static class ScadaPrincipalFactory
{
    public static ClaimsPrincipal Create(AuthenticatedUser user, string source, string authenticationType) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ScadaClaimTypes.Credential, user.CredentialId),
                new Claim(ScadaClaimTypes.Source, source)
            ],
            authenticationType, ClaimTypes.Name, ClaimTypes.Role));
}
