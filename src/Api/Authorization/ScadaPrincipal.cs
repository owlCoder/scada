using Application.Identity;
using Security.Authentication;

namespace Api.Authorization;

/// <summary>The caller resolved by the API key filter for the current request.</summary>
public sealed record ScadaPrincipal(AuthenticatedUser User)
{
    public const string ItemKey = "scada.principal";

    public Actor Actor => new(User.Username, User.Role);
}
