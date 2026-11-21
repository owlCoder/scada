using Application.Detection;
using Application.Events;
using Application.Identity;
using Domain.Process.Commands;

namespace Security.Detection;

public sealed class DetectionRuleAdministrationService(
    DetectionEngine detection, IDetectionRuleSettingsStore store, IEventPublisher events, TimeProvider time)
{
    private readonly SemaphoreSlim _updates = new(1, 1);

    public async Task<CommandResult> UpdateAsync(DetectionRuleSettings settings, Actor actor, CancellationToken cancellationToken)
    {
        var id = settings.RuleId;
        if (!actor.HasRole(AccessRole.Administrator))
        {
            const string message = "Administrator role required.";
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.AuthorizationDenied, EventSeverity.Medium,
                "detection-settings", message, actor.Name));
            return CommandResult.Rejected(id, CommandRejectionReason.NotAuthorized, message);
        }

        if (detection.Rules.All(rule => rule.Id != id))
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, "Detection rule not found.");

        if (!DetectionEngine.AreSettingsValid(settings))
            return CommandResult.Rejected(id, CommandRejectionReason.InvalidValue,
                "Threshold must be finite and non-negative, window must be 1–86,400 seconds and cooldown must be 0–86,400 seconds. Select a valid severity.");

        await _updates.WaitAsync(cancellationToken);
        try
        {
            if (!await store.SaveAsync(settings, cancellationToken))
                return CommandResult.Rejected(id, CommandRejectionReason.CommunicationUnavailable, "Could not persist detection rule settings. Try again.");
            detection.UpdateSettings(settings);
            return CommandResult.Success(id, "Detection rule settings saved.");
        }
        finally
        {
            _updates.Release();
        }
    }
}
