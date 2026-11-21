using Application.Alarms;
using Application.Commands;
using Application.Registers;
using Application.Training;
using Security.Accounts;

namespace Security.Scenarios;

/// <summary>Per-run state handed to every action: services, the run id and values captured by earlier actions.</summary>
public sealed class ScenarioExecutionContext(ScenarioServices services, Guid runId)
{
    private readonly Dictionary<string, long> _captured = new(StringComparer.Ordinal);

    public Guid RunId { get; } = runId;
    public ITrainingEnvironment Training => services.Training;
    public ControlCommandService Commands => services.Commands;
    public RegisterAccessService Registers => services.Registers;
    public AlarmService Alarms => services.Alarms;
    public UserAdministrationService Accounts => services.Accounts;

    /// <summary>The next sequence number the replay guard will accept for <paramref name="actor"/>.</summary>
    public long NextSequence(string actor) => services.ReplayGuard.LastSequence(actor) + 1;

    public bool Capture(string key, long sequenceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _captured[key] = sequenceNumber;
        return true;
    }

    public long? Recall(string key) => _captured.TryGetValue(key, out var value) ? value : null;
}
