using Application.Identity;

namespace Protocols.OpcUa;

public interface IOpcUaIdentityValidator
{
    Task<Actor?> ValidateAsync(string username, string password, CancellationToken cancellationToken);
}
