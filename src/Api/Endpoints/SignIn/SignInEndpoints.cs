using Api.Authentication;
using Application.Common;
using System.Security.Claims;
using Api.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Security.Authentication;

namespace Api.Endpoints.SignIn;

/// <summary>
/// Cookie sign-in for browsers: <c>GET/POST /account/login</c>, <c>POST /account/logout</c>, optional OIDC challenge and
/// <c>GET /account/antiforgery</c> for script clients. All failures show the same message, so usernames cannot be probed.
/// </summary>
public static class SignInEndpoints
{
    private const string GenericFailure = "Invalid username or password.";

    public static WebApplication Map(WebApplication app)
    {
        var account = app.MapGroup("/account");
        account.MapGet("/login", (string? returnUrl, string? error, HttpContext http, IAntiforgery antiforgery, OidcSettings oidc) =>
            Page(http, antiforgery, oidc, returnUrl, error == "sso" ? "Single sign-on failed or the account has no SCADA role." : null,
                StatusCodes.Status200OK));

        account.MapPost("/login", async ([FromForm] LoginForm form, HttpContext http, PasswordAuthenticator authenticator,
            IAntiforgery antiforgery, OidcSettings oidc, TimeProvider time) =>
        {
            var mode = form.Mode ?? SimulationMode.Water;
            if (!Enum.IsDefined(mode))
                return Results.BadRequest();
            var result = await authenticator.AuthenticateAsync(form.Username, form.Password, ClientSource.Of(http), http.RequestAborted);
            if (result.Status == AuthenticationStatus.Throttled)
                return Page(http, antiforgery, oidc, form.ReturnUrl, "Too many failed attempts. Try again in a minute.",
                    StatusCodes.Status429TooManyRequests, mode);
            if (result is not { Status: AuthenticationStatus.Succeeded, User: { } user })
                return Page(http, antiforgery, oidc, form.ReturnUrl, GenericFailure, StatusCodes.Status401Unauthorized, mode);

            var issued = time.GetUtcNow();
            var principal = ScadaPrincipalFactory.Create(user, ScadaSignInSources.Password, ScadaSignInSources.Password, issued, mode);
            await http.SignInAsync(ScadaAuthenticationSchemes.Cookie, principal,
                new AuthenticationProperties { IsPersistent = false, IssuedUtc = issued });
            var destination = SafeReturnUrl(form.ReturnUrl);
            return Results.LocalRedirect(destination == "/" && mode == SimulationMode.Grid ? "/grid" : destination);
        }).RequireRateLimiting(RateLimitSettings.LoginPolicy);

        // The antiforgery middleware only flags invalid requests; endpoints without form binding must check the result themselves.
        account.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest();
            await http.SignOutAsync(ScadaAuthenticationSchemes.Cookie);
            return Results.LocalRedirect("/account/login");
        }).DisableAntiforgery();

        account.MapPost("/mode", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest();
            var ticket = await http.AuthenticateAsync(ScadaAuthenticationSchemes.Cookie);
            if (!ticket.Succeeded || ticket.Principal is null)
                return Results.Unauthorized();
            var principal = ticket.Principal.Clone();
            var identity = (ClaimsIdentity)principal.Identity!;
            var current = identity.FindFirst(ScadaClaimTypes.Mode);
            var next = current?.Value == SimulationMode.Grid.ToString() ? SimulationMode.Water : SimulationMode.Grid;
            foreach (var claim in identity.FindAll(ScadaClaimTypes.Mode).ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(ScadaClaimTypes.Mode, next.ToString()));
            await http.SignInAsync(ScadaAuthenticationSchemes.Cookie, principal, ticket.Properties);
            return Results.LocalRedirect(next == SimulationMode.Grid ? "/grid" : "/");
        }).RequireAuthorization().DisableAntiforgery();

        account.MapGet("/login/oidc", (string? returnUrl, OidcSettings oidc) => oidc.Enabled
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [ScadaAuthenticationSchemes.Oidc])
            : Results.NotFound());

        account.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { token = tokens.RequestToken, headerName = tokens.HeaderName });
        });
        return app;
    }

    /// <summary>Only local paths are allowed, so the login form cannot be used as an open redirect.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && returnUrl.Length <= 512 && !returnUrl.Any(c => c is <= ' ' or '\u007f' or '\\')
            ? returnUrl
            : "/";

    private static IResult Page(HttpContext http, IAntiforgery antiforgery, OidcSettings oidc, string? returnUrl, string? error, int status, SimulationMode mode = SimulationMode.Water)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        var html = LoginPage.Render(tokens.FormFieldName, tokens.RequestToken ?? "", SafeReturnUrl(returnUrl), error,
            oidc.Enabled ? oidc.DisplayName : null, mode);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }
}
