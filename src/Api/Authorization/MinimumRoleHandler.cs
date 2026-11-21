using System.Security.Claims;
using Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Api.Authorization;

public sealed class MinimumRoleHandler : AuthorizationHandler<MinimumRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MinimumRoleRequirement requirement)
    {
        if (RoleOf(context.User) is { } role && role >= requirement.Role)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public static AccessRole? RoleOf(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && Enum.TryParse<AccessRole>(user.FindFirst(ClaimTypes.Role)?.Value, out var role) && Enum.IsDefined(role)
            ? role
            : null;
}
