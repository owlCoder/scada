using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Security.Authentication;

namespace Api.Authentication;

/// <summary>
/// Legacy <c>X-Api-Key</c> authentication. Keys are resolved through <see cref="ApiKeyAuthenticator"/> on every request, so
/// revoking a key or disabling an account takes effect immediately. A throttled source gets 429 instead of 401.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyAuthenticator authenticator) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    private const string ThrottledItem = "scada.auth.throttled";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[ScadaAuthenticationSchemes.ApiKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key))
            return AuthenticateResult.NoResult();

        var result = await authenticator.AuthenticateAsync(key, ClientSource.Of(Context), Context.RequestAborted);
        if (result is { Status: AuthenticationStatus.Succeeded, User: { } user })
        {
            var principal = ScadaPrincipalFactory.Create(user, ScadaSignInSources.ApiKey, Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        if (result.Status == AuthenticationStatus.Throttled)
            Context.Items[ThrottledItem] = true;
        return AuthenticateResult.Fail("Invalid API key.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(ThrottledItem))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = "60";
            return Task.CompletedTask;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ScadaAuthenticationSchemes.ApiKey;
        return Task.CompletedTask;
    }
}
