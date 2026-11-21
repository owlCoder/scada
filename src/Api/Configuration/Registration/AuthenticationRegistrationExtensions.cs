using System.Threading.RateLimiting;
using Api.Authentication;
using Api.Authorization;
using Api.RateLimiting;
using Application.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Security.Authentication;

namespace Api.Configuration.Registration;

/// <summary>
/// Authentication (session cookie, legacy X-Api-Key, optional OIDC), role policies, antiforgery and rate limiting.
/// API and hub paths answer 401/403 instead of redirecting to the login page.
/// </summary>
public static class AuthenticationRegistrationExtensions
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public static WebApplicationBuilder AddScadaAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var apiKeysEnabled = configuration.GetValue($"{ConfigurationSections.ApiKeys}:Enabled", true);
        var oidc = configuration.Bind<OidcSettings>(ConfigurationSections.Oidc);
        ThrowIfInvalid(oidc.Validate());
        services.AddSingleton(oidc);

        var lockout = configuration.Bind<PasswordLockoutOptions>(ConfigurationSections.Lockout);
        ThrowIfInvalid(lockout.Validate());
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
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = "/account/login";
                options.LogoutPath = "/account/logout";
                options.AccessDeniedPath = "/account/login";
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = CookieSessionValidator.ValidateAsync,
                    OnRedirectToLogin = context => ApiStatusOrRedirect(context, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => ApiStatusOrRedirect(context, StatusCodes.Status403Forbidden)
                };
            });
        if (apiKeysEnabled)
            authentication.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ScadaAuthenticationSchemes.ApiKey, null);
        if (oidc.Enabled)
            authentication.AddOpenIdConnect(ScadaAuthenticationSchemes.Oidc, oidc.DisplayName, options => ConfigureOidc(options, oidc));

        services.AddSingleton<IAuthorizationHandler, MinimumRoleHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        services.AddAuthorizationBuilder().AddScadaPolicies();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "scada.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        AddRateLimiting(builder);
        return builder;
    }

    private static Task ApiStatusOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        // Machine endpoints (REST and SignalR) answer with a status code; browser pages are redirected to sign-in.
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            context.Response.StatusCode = status;
        else
            context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
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

    private static IServiceCollection AddRateLimiting(WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.Bind<RateLimitSettings>(ConfigurationSections.RateLimiting);
        ThrowIfInvalid(settings.Validate());
        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton<RateLimitAuditor>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitions.For(context), _ => Window(settings.PermitsPerMinute)));
            options.AddPolicy(RateLimitSettings.LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientSource.Of(context), _ => Window(settings.LoginPermitsPerMinute)));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<RateLimitAuditor>()
                    .Record(RateLimitPartitions.For(context.HttpContext), context.HttpContext);
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static bool ThrowIfInvalid(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? true : throw new InvalidOperationException(string.Join(" ", errors));
}
