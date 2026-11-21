using Application.Identity;
using Security.Authentication;
using Security.Commands;

namespace Api.Authorization;

/// <summary>
/// API key authorisation (X-Api-Key) and per-credential rate limiting for minimal API endpoints. A missing, unknown
/// or insufficient key returns 401. The resolved caller is available through <see cref="Principal"/>.
/// </summary>
public static class EndpointAuthorizationExtensions
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, AccessRole role) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var authenticator = http.RequestServices.GetRequiredService<ApiKeyAuthenticator>();
            var source = $"ip:{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
            var result = await authenticator.AuthenticateAsync(http.Request.Headers[ApiKeyHeader].ToString(), source, http.RequestAborted);
            if (result.Status == AuthenticationStatus.Throttled)
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            if (result.User is not { } user || user.Role < role)
                return Results.Unauthorized();

            http.Items[ScadaPrincipal.ItemKey] = new ScadaPrincipal(user);
            return await next(context);
        });

    /// <summary>Limits state-changing requests per credential so changing the claimed actor cannot bypass it.</summary>
    public static TBuilder RequireCredentialRateLimit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Items[ScadaPrincipal.ItemKey] is not ScadaPrincipal principal)
                return Results.Unauthorized();

            var monitor = http.RequestServices.GetRequiredService<CommandMonitor>();
            return monitor.Check($"credential:{principal.User.CredentialId}")
                ? await next(context)
                : Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });

    public static ScadaPrincipal Principal(this HttpContext http) =>
        http.Items[ScadaPrincipal.ItemKey] as ScadaPrincipal
            ?? throw new InvalidOperationException("Endpoint is missing RequireRole.");

    public static Actor Actor(this HttpContext http) => http.Principal().Actor;
}
