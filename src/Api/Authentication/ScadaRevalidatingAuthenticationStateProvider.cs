using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Security.Authentication;

namespace Api.Authentication;

/// <summary>
/// Blazor circuits outlive single requests, so the HMI re-checks password sessions every minute: a disabled or locked
/// account, a changed password or a changed role ends the circuit's authentication and sends the user back to sign-in.
/// </summary>
public sealed class ScadaRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        if (user.FindFirst(ScadaClaimTypes.Source)?.Value != ScadaSignInSources.Password)
            return user.Identity?.IsAuthenticated == true;

        var issued = long.TryParse(user.FindFirst(ScadaClaimTypes.Issued)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;
        await using var scope = scopes.CreateAsyncScope();
        var authenticator = scope.ServiceProvider.GetRequiredService<PasswordAuthenticator>();
        var current = await authenticator.ValidateSessionAsync(user.Identity?.Name ?? "", issued, cancellationToken);
        return current is not null && user.IsInRole(current.Role.ToString());
    }
}
