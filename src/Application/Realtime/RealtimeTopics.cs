namespace Application.Realtime;

public static class RealtimeTopics
{
    public const string Plant = "plant";
    public const string Telemetry = "telemetry";
    public const string Controllers = "controllers";
    public const string Network = "network";
    public const string Alarms = "alarms";
    public const string Events = "events";
    public const string Incidents = "incidents";
    public const string Scenarios = "scenarios";
    public const string Detections = "detections";
    public const string Simulation = "simulation";

    public static IReadOnlyList<string> All { get; } =
        [Plant, Telemetry, Controllers, Network, Alarms, Events, Incidents, Scenarios, Detections, Simulation];
}
