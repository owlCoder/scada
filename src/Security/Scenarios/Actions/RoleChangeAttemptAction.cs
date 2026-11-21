using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>An account-management request made with the caller's own (possibly insufficient) privileges.</summary>
public sealed class RoleChangeAttemptAction(
    Actor actor,
    string targetUsername,
    AccountAttemptKind kind,
    AccessRole requestedRole = AccessRole.Administrator) : IScenarioAction
{
    public string Description => kind == AccountAttemptKind.ChangeRole
        ? $"{actor.Name} ({actor.Role}) tries to set {targetUsername} to {requestedRole}"
        : $"{actor.Name} ({actor.Role}) tries to rotate the API key of {targetUsername}";

    public async Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var result = kind == AccountAttemptKind.ChangeRole
            ? await context.Accounts.ChangeRoleAsync(targetUsername, requestedRole, actor, cancellationToken)
            : await context.Accounts.RotateApiKeyAsync(targetUsername, actor, cancellationToken);
        // Never surface a generated secret in the run log.
        return ScenarioActionResult.Done($"{result.Status}: {result.Message}");
    }
}
