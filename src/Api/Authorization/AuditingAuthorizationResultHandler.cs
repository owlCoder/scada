using Application.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Api.Authorization;

/// <summary>
/// Audits requests from authenticated callers whose role is too low (403) as <see cref="EventCategories.AuthorizationDenied"/>
/// events, which feed the unauthorized-access detection rule. Anonymous challenges are not audited here; failed
/// credentials are audited by the authenticators.
/// </summary>
public sealed class AuditingAuthorizationResultHandler(IEventPublisher events, TimeProvider time) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var required = policy.Requirements.OfType<MinimumRoleRequirement>().Select(r => r.Role.ToString()).FirstOrDefault() ?? "policy";
            var caller = MinimumRoleHandler.RoleOf(context.User)?.ToString() ?? "unknown";
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.AuthorizationDenied, EventSeverity.High, "api",
                $"Denied {context.Request.Method} {context.Request.Path}: requires {required}, caller is {caller}",
                context.User.Identity?.Name ?? "unknown"));
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
