namespace Security;

public sealed class AuthorizationService(AccessPolicy legacy, UserAccessPolicy users)
{
    public AuthenticatedUser? Authenticate(string? key)
    {
        var user = users.Authenticate(key);
        if (user is not null)
            return user;

        var role = legacy.Authenticate(key);
        if (role is null)
            return null;

        return new AuthenticatedUser($"legacy-{role.Value}", role.Value,
            ApiKeyValidator.Fingerprint(key!));
    }

    public bool Authorize(string? key, AccessRole required) =>
        Authenticate(key) is { } user && user.Role >= required;
}
