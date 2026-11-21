using Api.Authentication;
using Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Security.Authentication;

namespace IntegrationTests.Support;

/// <summary>Issues a real session cookie with the principal an OIDC callback would create.</summary>
public sealed class OidcSessionStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (context.Request.Path == "/test/oidc-session")
            {
                var user = new AuthenticatedUser("oidc-user", AccessRole.Viewer, "oidc:42");
                await context.SignInAsync(ScadaAuthenticationSchemes.Cookie,
                    ScadaPrincipalFactory.Create(user, ScadaSignInSources.Oidc, ScadaSignInSources.Oidc));
                return;
            }

            await continuation(context);
        });
        next(app);
    };
}
