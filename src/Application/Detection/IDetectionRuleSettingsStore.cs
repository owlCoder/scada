namespace Application.Detection;

public interface IDetectionRuleSettingsStore
{
    Task<IReadOnlyList<DetectionRuleSettings>> LoadAsync(CancellationToken cancellationToken);
    Task<bool> SaveAsync(DetectionRuleSettings settings, CancellationToken cancellationToken);
}
