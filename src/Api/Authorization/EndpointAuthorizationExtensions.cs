using Api.Authentication;
using Application.Identity;
using Security.Authentication;
using Security.Commands;

namespace Api.Authorization;

/// <summary>
/// Endpoint conventions on top of ASP.NET authorization. <see cref="RequireRole{TBuilder}"/> applies the role policy:
/// no or invalid credentials return 401, a role that is too low returns 403, a throttled client 429.
/// </summary>
public static class EndpointAuthorizationExtensions
{
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, AccessRole role) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(ScadaPolicies.For(role));

    /// <summary>Limits state-changing requests per credential so changing the claimed actor cannot bypass it.</summary>
    public static TBuilder RequireCredentialRateLimit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.User.FindFirst(ScadaClaimTypes.Credential)?.Value is not { Length: > 0 } credential)
                return Results.Unauthorized();

            var monitor = http.RequestServices.GetRequiredService<CommandMonitor>();
            return monitor.Check($"credential:{credential}")
                ? await next(context)
                : Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });

    public static ScadaPrincipal Principal(this HttpContext http)
    {
        var role = MinimumRoleHandler.RoleOf(http.User)
            ?? throw new InvalidOperationException("Endpoint is missing RequireRole or the caller has no role claim.");
        var name = http.User.Identity?.Name ?? throw new InvalidOperationException("Authenticated caller has no name.");
        var credential = http.User.FindFirst(ScadaClaimTypes.Credential)?.Value ?? name;
        return new ScadaPrincipal(new AuthenticatedUser(name, role, credential));
    }

    public static Actor Actor(this HttpContext http) => http.Principal().Actor;
}
