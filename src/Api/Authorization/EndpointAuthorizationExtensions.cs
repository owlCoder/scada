using Security;

namespace Api.Authorization;

public static class EndpointAuthorizationExtensions
{
    public static RouteGroupBuilder RequireRole(this RouteGroupBuilder group, AccessRole role)
    {
        group.AddEndpointFilter((context, next) => AuthorizeAsync(context, next, role));
        return group;
    }

    public static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder endpoint, AccessRole role)
    {
        endpoint.AddEndpointFilter((context, next) => AuthorizeAsync(context, next, role));
        return endpoint;
    }

    private static async ValueTask<object?> AuthorizeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        AccessRole role)
    {
        var authorization = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
        var apiKey = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
        if (!authorization.Authorize(apiKey, role))
            return Results.Unauthorized();

        return await next(context);
    }
}
