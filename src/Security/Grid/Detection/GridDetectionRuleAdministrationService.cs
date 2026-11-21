using Application.Detection;
using Application.Events;
using Application.Identity;
using Domain.Process.Commands;
using Microsoft.Extensions.Logging;
using Security.Detection;

namespace Security.Grid.Detection;

/// <summary>Administrator-only tuning of grid detection rules; persists through <see cref="IDetectionRuleSettingsStore"/> and audits.</summary>
public sealed class GridDetectionRuleAdministrationService(
    GridDetectionEngine detection,
    IDetectionRuleSettingsStore store,
    IEventPublisher events,
    TimeProvider time,
    ILogger<GridDetectionRuleAdministrationService> logger)
{
    private readonly SemaphoreSlim _updates = new(1, 1);

    public async Task<CommandResult> UpdateAsync(DetectionRuleSettings settings, Actor actor, CancellationToken cancellationToken)
    {
        var id = settings.RuleId;
        if (!actor.HasRole(AccessRole.Administrator))
        {
            const string message = "Administrator role required.";
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.AuthorizationDenied, EventSeverity.Medium,
                "grid-detection-settings", message, actor.Name));
            return CommandResult.Rejected(id, CommandRejectionReason.NotAuthorized, message);
        }

        if (detection.Rules.All(rule => rule.Id != id))
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, "Grid detection rule not found.");

        if (!DetectionEngine.AreSettingsValid(settings))
            return CommandResult.Rejected(id, CommandRejectionReason.InvalidValue,
                "Threshold must be finite and non-negative, window must be 1–86,400 seconds and cooldown must be 0–86,400 seconds. Select a valid severity.");

        await _updates.WaitAsync(cancellationToken);
        try
        {
            if (!await store.SaveAsync(settings, cancellationToken))
                return CommandResult.Rejected(id, CommandRejectionReason.CommunicationUnavailable, "Could not persist grid detection rule settings. Try again.");
            detection.UpdateSettings(settings);
            logger.LogInformation("Grid detection rule {RuleId} settings updated by {Actor}", id, actor.Name);
            return CommandResult.Success(id, "Grid detection rule settings saved.");
        }
        finally
        {
            _updates.Release();
        }
    }
}
