using Application.Identity;
using Protocols.OpcUa;
using Security.Authentication;

namespace Api.Authentication;

internal sealed class OpcUaIdentityValidator(PasswordAuthenticator passwords) : IOpcUaIdentityValidator
{
    public async Task<Actor?> ValidateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var result = await passwords.AuthenticateAsync(username, password, "opcua:loopback", cancellationToken);
        return result.User is { } user ? new Actor(user.Username, user.Role) : null;
    }
}
