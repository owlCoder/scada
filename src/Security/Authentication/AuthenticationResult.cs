namespace Security.Authentication;

public sealed record AuthenticationResult(AuthenticationStatus Status, AuthenticatedUser? User)
{
    public bool Succeeded => Status == AuthenticationStatus.Succeeded;
}
