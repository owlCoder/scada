using Application.Identity;

namespace Infrastructure.Persistence.Postgres.Entities;

/// <summary><see cref="UsernameKey"/> is the lower-cased username, which makes usernames case-insensitive.</summary>
public sealed class UserAccountEntity
{
    public string UsernameKey { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public AccessRole Role { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? ApiKeyHash { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public long Version { get; set; }

    public static string KeyOf(string username) => username.Trim().ToLowerInvariant();

    public UserAccountEntity Apply(UserAccount a)
    {
        UsernameKey = KeyOf(a.Username);
        Username = a.Username.Trim();
        DisplayName = a.DisplayName;
        Role = a.Role;
        PasswordHash = a.PasswordHash;
        ApiKeyHash = a.ApiKeyHash;
        IsEnabled = a.IsEnabled;
        CreatedAt = Timestamps.Utc(a.CreatedAt);
        PasswordChangedAt = Timestamps.Utc(a.PasswordChangedAt);
        LastLoginAt = Timestamps.Utc(a.LastLoginAt);
        FailedLoginCount = a.FailedLoginCount;
        LockedUntil = Timestamps.Utc(a.LockedUntil);
        return this;
    }

    public UserAccount ToRecord() => new(Username, DisplayName, Role, PasswordHash, ApiKeyHash, IsEnabled, CreatedAt,
        PasswordChangedAt, LastLoginAt, FailedLoginCount, LockedUntil) { Version = Version };
}
