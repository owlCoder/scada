namespace Api.Configuration;

public static class SecurityHeadersExtensions
{
    public static WebApplication UseScadaSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            // Same values the antiforgery system requires, so it does not have to override them on pages with tokens.
            context.Response.Headers["Cache-Control"] = "no-cache, no-store";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            await next();
        });
        return app;
    }
}
