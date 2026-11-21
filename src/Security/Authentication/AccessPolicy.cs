using Application.Identity;

namespace Security.Authentication;

public sealed class AccessPolicy
{
    private readonly IReadOnlyDictionary<AccessRole, string> _keys;

    public AccessPolicy(IReadOnlyDictionary<AccessRole, string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Keys.Any(role => !Enum.IsDefined(role)))
            throw new ArgumentException("API key roles must be valid.", nameof(keys));

        var active = keys.Values.Where(key => !string.IsNullOrWhiteSpace(key)).ToArray();
        if (active.Distinct(StringComparer.Ordinal).Count() != active.Length)
            throw new ArgumentException("Different roles must have distinct API keys.", nameof(keys));

        _keys = new Dictionary<AccessRole, string>(keys);
    }

    public AccessRole? Authenticate(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return null;

        foreach (var role in new[] { AccessRole.Administrator, AccessRole.Operator, AccessRole.Viewer })
        {
            if (_keys.TryGetValue(role, out var expected) &&
                ApiKeyValidator.Matches(expected, supplied))
                return role;
        }

        return null;
    }

    public bool Authorize(string? supplied, AccessRole required)
    {
        if (!Enum.IsDefined(required))
            return false;

        var role = Authenticate(supplied);
        return role is not null && role.Value >= required;
    }
}
