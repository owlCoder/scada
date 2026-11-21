using Application.Identity;

namespace Security.Authentication;

/// <summary>
/// A configured account. At least one credential is required: an API key, a password, or a pre-computed
/// <see cref="PasswordHasher"/> hash (preferred outside development, so no plain password sits in configuration).
/// </summary>
public sealed record UserCredential(string Username, AccessRole Role, string ApiKey, string? Password = null, string? PasswordHash = null)
{
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
    public bool HasPassword => !string.IsNullOrEmpty(Password) || !string.IsNullOrWhiteSpace(PasswordHash);
}
