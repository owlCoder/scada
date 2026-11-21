using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Security.Authentication;

namespace Api.Authentication;

/// <summary>
/// Re-validates password sessions against the account store on every request: a disabled or locked account, or a password
/// changed after sign-in, ends the session; a role change is applied immediately. OIDC sessions are owned by the identity
/// provider and expire with the cookie.
/// </summary>
public static class CookieSessionValidator
{
    public static async Task<bool> ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.FindFirst(ScadaClaimTypes.Source)?.Value != ScadaSignInSources.Password)
            return true;

        var username = context.Principal.Identity?.Name;
        var issued = context.Properties.IssuedUtc ?? DateTimeOffset.MinValue;
        var authenticator = context.HttpContext.RequestServices.GetRequiredService<PasswordAuthenticator>();
        var user = string.IsNullOrEmpty(username) ? null : await authenticator.ValidateSessionAsync(username, issued, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(ScadaAuthenticationSchemes.Cookie);
            return false;
        }

        if (!context.Principal.IsInRole(user.Role.ToString()))
        {
            context.ReplacePrincipal(ScadaPrincipalFactory.Create(user, ScadaSignInSources.Password, ScadaSignInSources.Password));
            context.ShouldRenew = true;
        }

        return true;
    }
}
