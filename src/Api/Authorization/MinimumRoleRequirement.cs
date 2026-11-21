using Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Api.Authorization;

/// <summary>Role hierarchy: Administrator ⊃ Operator ⊃ Viewer.</summary>
public sealed record MinimumRoleRequirement(AccessRole Role) : IAuthorizationRequirement;
