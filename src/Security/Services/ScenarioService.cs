using Application;

namespace Security;

public sealed class ScenarioService(IEventStore events)
{
    private static readonly ScenarioDefinition[] Available =
    [
        new("unauthorized-command", "Unauthorized command", "Simulated unauthorized control attempt"),
        new("sensor-spoofing", "Sensor spoofing", "Simulated mismatch between sensor and process state"),
        new("register-tampering", "Register tampering", "Simulated attempt to modify a protected register")
    ];

    public IReadOnlyList<ScenarioDefinition> GetAvailable() => Available;

    public bool Execute(string id, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Operator is required.", nameof(actor));

        var scenario = Available.FirstOrDefault(item => item.Id == id);
        if (scenario is null)
            return false;

        events.Append(new(DateTimeOffset.UtcNow, "SecurityAlert",
            $"Scenario: {scenario.Name}. {scenario.Description}", actor));
        return true;
    }
}
