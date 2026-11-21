namespace Application.Identity;

public sealed record UserAccount(
    string Username,
    string DisplayName,
    AccessRole Role,
    string PasswordHash,
    string? ApiKeyHash,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PasswordChangedAt,
    DateTimeOffset? LastLoginAt,
    int FailedLoginCount,
    DateTimeOffset? LockedUntil);
