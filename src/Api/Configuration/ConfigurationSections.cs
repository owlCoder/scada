namespace Api.Configuration;

public static class ConfigurationSections
{
    public const string Storage = "Storage";
    public const string Persistence = "Persistence";
    public const string Retention = "Retention";
    public const string Simulation = "Simulation";
    public const string SimulationHost = "Simulation:Host";
    public const string Registers = "Registers";
    public const string CommandRate = "Security:CommandRate";
    public const string Users = "Security:Users";
    public const string Modbus = "Protocols:Modbus";
    public const string OpcUa = "Protocols:OpcUa";
    public const string Iec104 = "Protocols:Iec104";
    public const string ApiKeys = "Security:ApiKeys";
    public const string Lockout = "Security:Lockout";
    public const string DevelopmentAccounts = "Security:DevelopmentAccounts";
    public const string Oidc = "Authentication:Oidc";
    public const string RateLimiting = "RateLimiting";
    public const string Observability = "Observability";

    public static T Bind<T>(this IConfiguration configuration, string section) where T : new()
    {
        var value = new T();
        configuration.GetSection(section).Bind(value);
        return value;
    }
}
