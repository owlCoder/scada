namespace Security;

public sealed record UserCredential(string Username, AccessRole Role, string ApiKey);
