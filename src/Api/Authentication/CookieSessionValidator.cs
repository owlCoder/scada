using System.Globalization;
using Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Security.Authentication;

namespace Api.Authentication;

/// <summary>
/// Re-validates password sessions against the account store on every request: a disabled or locked account, or a password
/// changed after sign-in, ends the session; a role change is applied immediately. Every session has an absolute
/// lifetime measured from its original sign-in.
/// </summary>
public static class CookieSessionValidator
{
    private const string StartedAt = "scada.session-started-at";

    public static Task StartSessionAsync(CookieSigningInContext context)
    {
        var now = context.Options.TimeProvider!.GetUtcNow();
        if (context.Properties.GetString(StartedAt) is null)
            context.Properties.SetString(StartedAt, now.ToString("O", CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public static async Task<bool> ValidateAsync(CookieValidatePrincipalContext context)
    {
        var settings = context.HttpContext.RequestServices.GetRequiredService<SessionSettings>();
        var now = context.Options.TimeProvider!.GetUtcNow();
        if (!DateTimeOffset.TryParseExact(context.Properties.GetString(StartedAt), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var startedAt)
            || startedAt > now || now - startedAt >= settings.AbsoluteLifetime)
            return await RejectAsync(context);

        if (context.Principal is not { } principal)
            return await RejectAsync(context);
        if (principal.FindFirst(ScadaClaimTypes.Source)?.Value != ScadaSignInSources.Password)
            return true;

        var username = principal.Identity?.Name;
        var authenticator = context.HttpContext.RequestServices.GetRequiredService<PasswordAuthenticator>();
        var user = string.IsNullOrEmpty(username)
            ? null
            : await authenticator.ValidateSessionAsync(username, startedAt, context.HttpContext.RequestAborted);
        if (user is null)
            return await RejectAsync(context);

        if (!principal.IsInRole(user.Role.ToString()))
        {
            var mode = Enum.TryParse<SimulationMode>(principal.FindFirst(ScadaClaimTypes.Mode)?.Value, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : SimulationMode.Water;
            context.ReplacePrincipal(ScadaPrincipalFactory.Create(user, ScadaSignInSources.Password, ScadaSignInSources.Password, startedAt, mode));
            context.ShouldRenew = true;
        }

        return true;
    }

    private static async Task<bool> RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(ScadaAuthenticationSchemes.Cookie);
        return false;
    }
}
