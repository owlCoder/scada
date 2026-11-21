using Application.Identity;
using Protocols.OpcUa;
using Security.Authentication;

namespace Api.Authentication;

internal sealed class OpcUaIdentityValidator(PasswordAuthenticator passwords) : IOpcUaIdentityValidator
{
    // The server binds to 127.0.0.1 only, so every client shares the loopback source; a per-endpoint key would be
    // identical. Per-account lockout still applies on top of this throttle.
    public async Task<Actor?> ValidateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var result = await passwords.AuthenticateAsync(username, password, "opcua:loopback", cancellationToken);
        return result.User is { } user ? new Actor(user.Username, user.Role) : null;
    }
}
