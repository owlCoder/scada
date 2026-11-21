using System.Security.Cryptography;
using System.Text;

using Application.Identity;

namespace Security.Authentication;

public sealed class UserAccessPolicy
{
    private readonly UserCredential[] _users;

    public UserAccessPolicy(IEnumerable<UserCredential> users)
    {
        _users = users.ToArray();
        if (_users.Any(user => string.IsNullOrWhiteSpace(user.Username) || !(user.HasApiKey || user.HasPassword) || !Enum.IsDefined(user.Role)))
            throw new ArgumentException("Each user must have a name, a valid role and an API key or password.", nameof(users));

        var keys = _users.Where(user => user.HasApiKey).Select(user => user.ApiKey).ToArray();
        if (_users.Select(user => user.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _users.Length ||
            keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Usernames and API keys must be unique.", nameof(users));
    }

    public AuthenticatedUser? Authenticate(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return null;

        foreach (var user in _users.Where(u => u.HasApiKey))
        {
            if (ApiKeyValidator.Matches(user.ApiKey, supplied))
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(user.ApiKey));
                return new AuthenticatedUser(user.Username, user.Role, Convert.ToHexString(hash));
            }
        }

        return null;
    }
}
