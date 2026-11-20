namespace Security;

public sealed record AuthenticatedUser(string Username, AccessRole Role, string CredentialId);
