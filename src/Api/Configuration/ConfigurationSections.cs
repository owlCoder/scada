namespace Api.Configuration;

public static class ConfigurationSections
{
    public const string Storage = "Storage";
    public const string Persistence = "Persistence";
    public const string Simulation = "Simulation";
    public const string SimulationHost = "Simulation:Host";
    public const string Registers = "Registers";
    public const string CommandRate = "Security:CommandRate";
    public const string Users = "Security:Users";
    public const string Modbus = "Protocols:Modbus";

    public static T Bind<T>(this IConfiguration configuration, string section) where T : new()
    {
        var value = new T();
        configuration.GetSection(section).Bind(value);
        return value;
    }
}
