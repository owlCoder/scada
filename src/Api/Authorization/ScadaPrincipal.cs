using Application.Identity;
using Security.Authentication;

namespace Api.Authorization;

/// <summary>The authenticated caller of the current request, as seen by endpoint handlers.</summary>
public sealed record ScadaPrincipal(AuthenticatedUser User)
{
    public Actor Actor => new(User.Username, User.Role);
}
