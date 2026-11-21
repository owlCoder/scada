namespace Application.Events;

public static class EventCategories
{
    public const string Audit = "Audit";
    public const string ProcessAlarm = "ProcessAlarm";
    public const string SafetyInterlock = "SafetyInterlock";
    public const string SafetyViolation = "SafetyViolation";
    public const string SecurityAlert = "SecurityAlert";
    public const string CommandRejected = "CommandRejected";
    public const string AuthorizationDenied = "AuthorizationDenied";
    public const string Authentication = "Authentication";
    public const string Communication = "Communication";
    public const string Controller = "Controller";
    public const string DeviceChange = "DeviceChange";
    public const string Detection = "Detection";
    public const string Scenario = "Scenario";
    public const string Network = "Network";
    public const string Integrity = "Integrity";
    public const string Protocol = "Protocol";
    public const string System = "System";
}
