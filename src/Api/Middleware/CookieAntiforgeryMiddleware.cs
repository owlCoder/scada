using Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;

namespace Api.Middleware;

/// <summary>
/// Cookie-authenticated callers must send an antiforgery token (header <c>X-CSRF-TOKEN</c>, from <c>GET /account/antiforgery</c>)
/// with state-changing API requests. API key callers are not affected: browsers never attach that header on their own.
/// </summary>
public sealed class CookieAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (RequiresToken(context) && !await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "A valid antiforgery token is required." });
            return;
        }

        await next(context);
    }

    private static bool RequiresToken(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api")
        && !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        && ScadaSignInSources.UsesCookie(context.User.FindFirst(ScadaClaimTypes.Source)?.Value);
}
