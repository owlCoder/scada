using Api.Authorization;
using Api.Contracts;
using Application.Identity;
using Security.Accounts;

namespace Api.Endpoints.Accounts;

public static class AccountEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/accounts", async (UserAdministrationService accounts, CancellationToken cancellationToken) =>
            Results.Ok(await accounts.ListAsync(cancellationToken))).RequireRole(AccessRole.Administrator);

        var write = app.MapGroup("/api/accounts/{username}").RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        write.MapPost("/role", async (string username, RoleChangeRequest request, UserAdministrationService accounts, HttpContext http,
            CancellationToken cancellationToken) => (await accounts.ChangeRoleAsync(username, request.Role, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/enabled", async (string username, EnabledRequest request, UserAdministrationService accounts, HttpContext http,
            CancellationToken cancellationToken) => (await accounts.SetEnabledAsync(username, request.Enabled, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/password/rotate", async (string username, UserAdministrationService accounts, HttpContext http,
            CancellationToken cancellationToken) => (await accounts.RotatePasswordAsync(username, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/api-key/rotate", async (string username, UserAdministrationService accounts, HttpContext http,
            CancellationToken cancellationToken) => (await accounts.RotateApiKeyAsync(username, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/api-key/revoke", async (string username, UserAdministrationService accounts, HttpContext http,
            CancellationToken cancellationToken) => (await accounts.RevokeApiKeyAsync(username, http.Actor(), cancellationToken)).ToHttp());
        return app;
    }
}
