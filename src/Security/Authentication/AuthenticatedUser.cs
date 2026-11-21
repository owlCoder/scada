using Application.Identity;

namespace Security.Authentication;

public sealed record AuthenticatedUser(string Username, AccessRole Role, string CredentialId);
