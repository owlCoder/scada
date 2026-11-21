using Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Api.Authorization;

public static class ScadaPolicies
{
    public const string Viewer = nameof(AccessRole.Viewer);
    public const string Operator = nameof(AccessRole.Operator);
    public const string Administrator = nameof(AccessRole.Administrator);

    public static string For(AccessRole role) => role.ToString();

    public static AuthorizationBuilder AddScadaPolicies(this AuthorizationBuilder builder)
    {
        foreach (var role in Enum.GetValues<AccessRole>())
            builder.AddPolicy(For(role), policy => policy.RequireAuthenticatedUser().AddRequirements(new MinimumRoleRequirement(role)));
        return builder;
    }
}
