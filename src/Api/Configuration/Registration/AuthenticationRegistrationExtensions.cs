using Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Security.Authentication;

namespace Api.Configuration.Registration;

/// <summary>Registers session cookies, antiforgery and optional API key authentication.</summary>
public static class AuthenticationRegistrationExtensions
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public static WebApplicationBuilder AddScadaAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var apiKeysEnabled = configuration.GetValue($"{ConfigurationSections.ApiKeys}:Enabled", true);
        var securePolicy = configuration.GetValue("Security:Cookies:RequireSecure", false)
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        var session = configuration.Bind<SessionSettings>("Security:Session");
        if (session.AbsoluteLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Security:Session:AbsoluteLifetime must be positive.");
        services.AddSingleton(session);

        var lockout = configuration.Bind<PasswordLockoutOptions>(ConfigurationSections.Lockout);
        var errors = lockout.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        services.AddSingleton(lockout);
        services.AddSingleton<PasswordAuthenticator>();

        var authentication = services.AddAuthentication(ScadaAuthenticationSchemes.Default)
            .AddPolicyScheme(ScadaAuthenticationSchemes.Default, "API key or session", options =>
                options.ForwardDefaultSelector = context =>
                    apiKeysEnabled && context.Request.Headers.ContainsKey(ScadaAuthenticationSchemes.ApiKeyHeader)
                        ? ScadaAuthenticationSchemes.ApiKey
                        : ScadaAuthenticationSchemes.Cookie)
            .AddCookie(ScadaAuthenticationSchemes.Cookie, options =>
            {
                options.Cookie.Name = "scada.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = securePolicy;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = "/account/login";
                options.LogoutPath = "/account/logout";
                options.AccessDeniedPath = "/account/login";
                options.Events = new CookieAuthenticationEvents
                {
                    OnSigningIn = CookieSessionValidator.StartSessionAsync,
                    OnValidatePrincipal = CookieSessionValidator.ValidateAsync,
                    OnRedirectToLogin = context => ApiStatusOrRedirect(context, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => ApiStatusOrRedirect(context, StatusCodes.Status403Forbidden)
                };
            });
        if (apiKeysEnabled)
            authentication.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ScadaAuthenticationSchemes.ApiKey, null);

        services.AddOptions<CookieAuthenticationOptions>(ScadaAuthenticationSchemes.Cookie)
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "scada.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
        });
        return builder;
    }

    private static Task ApiStatusOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.StatusCode = status;
        else
            context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
