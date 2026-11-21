using System.Security.Cryptography;

namespace Security.Authentication;

/// <summary>PBKDF2-SHA256 password hashing. Format: <c>pbkdf2-sha256$iterations$salt$hash</c> (Base64).</summary>
public static class PasswordHasher
{
    public const int MinimumLength = 12;
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "pbkdf2-sha256";

    private static readonly byte[] TimingSalt = new byte[SaltSize];

    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            throw new ArgumentException($"Passwords must contain at least {MinimumLength} characters.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        if (encoded?.Split('$') is [Prefix, var workFactor, var saltText, var hashText]
            && int.TryParse(workFactor, out var iterations) && iterations is >= 10_000 and <= 1_000_000)
        {
            try
            {
                var salt = Convert.FromBase64String(saltText);
                var expected = Convert.FromBase64String(hashText);
                if (expected.Length > 0)
                {
                    var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }
            }
            catch (FormatException)
            {
                // Use the same fallback work as for an absent hash.
            }
        }

        // Unknown accounts and missing or malformed hashes still do one standard derivation.
        _ = Rfc2898DeriveBytes.Pbkdf2(password, TimingSalt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return false;
    }

    public static string GeneratePassword(int length = 20)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";
        return RandomNumberGenerator.GetString(alphabet, Math.Max(MinimumLength, length));
    }

    public static string GenerateApiKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
