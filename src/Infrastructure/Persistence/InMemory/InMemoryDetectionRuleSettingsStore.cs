using Application.Detection;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryDetectionRuleSettingsStore : IDetectionRuleSettingsStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, DetectionRuleSettings> _settings = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<DetectionRuleSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<DetectionRuleSettings>>(_settings.Values.OrderBy(s => s.RuleId, StringComparer.Ordinal).ToArray());
    }

    public Task<bool> SaveAsync(DetectionRuleSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
            _settings[settings.RuleId] = settings;
        return Task.FromResult(true);
    }
}
