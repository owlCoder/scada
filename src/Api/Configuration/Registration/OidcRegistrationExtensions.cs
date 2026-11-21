using Api.Authentication;
using Application.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Api.Configuration.Registration;

public static class OidcRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaOidc(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.Bind<OidcSettings>(ConfigurationSections.Oidc);
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        builder.Services.AddSingleton(settings);
        if (settings.Enabled)
            builder.Services.AddAuthentication().AddOpenIdConnect(ScadaAuthenticationSchemes.Oidc, settings.DisplayName,
                options => ConfigureOidc(options, settings));
        return builder;
    }

    public static WebApplication WarnIfOidcMetadataAllowsHttp(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<OidcSettings>();
        if (settings.Enabled && !settings.RequireHttpsMetadata)
            app.Logger.LogWarning("OIDC is enabled with Authentication:Oidc:RequireHttpsMetadata=false. OIDC metadata can be fetched over HTTP.");
        return app;
    }

    private static OpenIdConnectOptions ConfigureOidc(OpenIdConnectOptions options, OidcSettings settings)
    {
        options.SignInScheme = ScadaAuthenticationSchemes.Cookie;
        options.Authority = settings.Authority;
        if (!string.IsNullOrWhiteSpace(settings.MetadataAddress))
            options.MetadataAddress = settings.MetadataAddress;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.CallbackPath = "/signin-oidc";
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = context =>
            {
                var events = context.HttpContext.RequestServices.GetRequiredService<IEventPublisher>();
                var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                if (context.Principal is null || OidcRoleMapper.Map(context.Principal, settings) is not { } user)
                {
                    events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Authentication, EventSeverity.Medium, "oidc",
                        "Single sign-on rejected: the identity has no SCADA role", ClientSource.Of(context.HttpContext)));
                    context.Fail("The identity has no SCADA role.");
                    return Task.CompletedTask;
                }

                context.Principal = ScadaPrincipalFactory.Create(user, ScadaSignInSources.Oidc, ScadaSignInSources.Oidc);
                events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Authentication, EventSeverity.Information, "oidc",
                    $"{user.Username} signed in through single sign-on", user.Username));
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/account/login?error=sso");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
        return options;
    }
}
