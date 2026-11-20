using System.Security.Cryptography;
using System.Text;

namespace Security;

public sealed class UserAccessPolicy
{
    private readonly UserCredential[] _users;

    public UserAccessPolicy(IEnumerable<UserCredential> users)
    {
        _users = users.ToArray();
        if (_users.Any(user => string.IsNullOrWhiteSpace(user.Username) ||
            string.IsNullOrWhiteSpace(user.ApiKey) || !Enum.IsDefined(user.Role)))
            throw new ArgumentException("Each user must have a name, key and valid role.", nameof(users));

        if (_users.Select(user => user.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _users.Length ||
            _users.Select(user => user.ApiKey).Distinct(StringComparer.Ordinal).Count() != _users.Length)
            throw new ArgumentException("Usernames and API keys must be unique.", nameof(users));
    }

    public AuthenticatedUser? Authenticate(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return null;

        foreach (var user in _users)
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
