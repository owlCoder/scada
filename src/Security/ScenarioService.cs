using Application;

namespace Security;

public sealed record ScenarioDefinition(string Id, string Name, string Description);

public sealed class ScenarioService(IEventStore events)
{
    private static readonly ScenarioDefinition[] Available =
    [
        new("unauthorized-command", "Неовлашћена команда", "Симулиран покушај неовлашћеног управљања"),
        new("sensor-spoofing", "Лажно мерење", "Симулиран несклад између сензора и стања процеса"),
        new("register-tampering", "Измена регистра", "Симулиран покушај измене заштићеног регистра")
    ];

    public IReadOnlyList<ScenarioDefinition> GetAvailable() => Available;

    public bool Execute(string id, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Оператер је обавезан.", nameof(actor));

        var scenario = Available.FirstOrDefault(item => item.Id == id);
        if (scenario is null)
            return false;

        events.Append(new(DateTimeOffset.UtcNow, "SecurityAlert",
            $"Сценарио: {scenario.Name}. {scenario.Description}", actor));
        return true;
    }
}
