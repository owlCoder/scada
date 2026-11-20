using System.Security.Cryptography;
using System.Text;

namespace Security;

public enum AccessRole { Viewer, Operator, Administrator }

public sealed class AccessPolicy
{
    private readonly IReadOnlyDictionary<AccessRole, string> _keys;

    public AccessPolicy(IReadOnlyDictionary<AccessRole, string> keys) => _keys = keys;

    public AccessRole? Authenticate(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return null;

        foreach (var role in new[] { AccessRole.Administrator, AccessRole.Operator, AccessRole.Viewer })
        {
            if (_keys.TryGetValue(role, out var expected) && !string.IsNullOrWhiteSpace(expected)
                && Matches(expected, supplied))
                return role;
        }

        return null;
    }

    public bool Authorize(string? supplied, AccessRole required)
    {
        var role = Authenticate(supplied);
        return role is not null && role.Value >= required;
    }

    private static bool Matches(string expected, string supplied)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
