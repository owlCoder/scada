using System.Security.Claims;
using Security.Authentication;
using Application.Common;

namespace Api.Authentication;

public static class ScadaPrincipalFactory
{
    public static ClaimsPrincipal Create(AuthenticatedUser user, string source, string authenticationType, DateTimeOffset? issuedAt = null, SimulationMode mode = SimulationMode.Water)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ScadaClaimTypes.Credential, user.CredentialId),
            new(ScadaClaimTypes.Source, source),
            new(ScadaClaimTypes.Mode, mode.ToString())
        ];
        if (issuedAt is { } issued)
            claims.Add(new Claim(ScadaClaimTypes.Issued, issued.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }
}
