using Application.Identity;

namespace Security.Accounts;

public sealed record AccountSummary(
    string Username,
    string DisplayName,
    AccessRole Role,
    bool IsEnabled,
    bool HasApiKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PasswordChangedAt,
    DateTimeOffset? LastLoginAt,
    bool IsLocked);
