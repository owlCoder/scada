using Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Api.Configuration.Registration;

public static class AuthorizationRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaAuthorization(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<IAuthorizationHandler, MinimumRoleHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        services.AddAuthorizationBuilder().AddScadaPolicies();
        return builder;
    }
}
