using Application.Identity;

namespace Security.Authentication;

public sealed record UserCredential(string Username, AccessRole Role, string ApiKey);
