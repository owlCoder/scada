using Application.Common;

namespace Hmi.Services.Shell;

/// <summary>The mode-aware list of displays shown in the icon rail and the Ctrl+K quick switcher.</summary>
public static class DisplayCatalog
{
    public static IReadOnlyList<DisplayEntry> For(SimulationMode mode) => mode == SimulationMode.Grid ? Grid : Water;

    private static IReadOnlyList<DisplayEntry> Water { get; } =
    [
        new("Overview", "", DisplayEntry.ProcessGroup, "overview", Short: "Overview"),
        new("Plant diagram", "diagram", DisplayEntry.ProcessGroup, "diagram", Short: "Diagram"),
        new("Tanks", "tanks", DisplayEntry.ProcessGroup, "tank", Short: "Tanks"),
        new("Pumps", "pumps", DisplayEntry.ProcessGroup, "pump", Short: "Pumps"),
        new("Valves", "valves", DisplayEntry.ProcessGroup, "valve", Short: "Valves"),
        new("Sensors", "sensors", DisplayEntry.ProcessGroup, "sensor", Short: "Sensors"),
        new("PLC / RTU", "controllers", DisplayEntry.ProcessGroup, "controller", Short: "PLC"),
        new("Trends", "trends", DisplayEntry.ProcessGroup, "trend", Short: "Trends"),
        new("Alarms", "alarms", DisplayEntry.OperationsGroup, "alarm", Short: "Alarms"),
        new("Events", "events", DisplayEntry.OperationsGroup, "events", Short: "Events"),
        new("Incidents", "incidents", DisplayEntry.OperationsGroup, "incident", Short: "Incidents"),
        new("Scenarios", "scenarios", DisplayEntry.OperationsGroup, "scenario", Short: "Scenarios"),
        new("Communications", "communications", DisplayEntry.SystemGroup, "comm", Short: "Comms"),
        new("Registers", "registers", DisplayEntry.SystemGroup, "register", Short: "Registers"),
        new("System health", "health", DisplayEntry.SystemGroup, "health", Short: "Health"),
        new("My account", "identity", DisplayEntry.SystemGroup, "account", Short: "Account"),
        new("Accounts & rules", "accounts", DisplayEntry.AdministrationGroup, "admin", AdminOnly: true, Short: "Admins")
    ];

    private static IReadOnlyList<DisplayEntry> Grid { get; } =
    [
        new("Grid overview", "grid", DisplayEntry.ProcessGroup, "overview", Short: "Overview"),
        new("Single-line diagram", "grid/diagram", DisplayEntry.ProcessGroup, "sld", Short: "Diagram"),
        new("Feeders", "grid/feeders", DisplayEntry.ProcessGroup, "feeder", Short: "Feeders"),
        new("Transformers", "grid/transformers", DisplayEntry.ProcessGroup, "transformer", Short: "Xformers"),
        new("Switches", "grid/switches", DisplayEntry.ProcessGroup, "switch", Short: "Switches"),
        new("Loads", "grid/loads", DisplayEntry.ProcessGroup, "load", Short: "Loads"),
        new("Protection", "grid/protection", DisplayEntry.ProcessGroup, "protection", Short: "Relays"),
        new("Measurements", "grid/measurements", DisplayEntry.ProcessGroup, "sensor", Short: "Measure"),
        new("Alarms", "grid/alarms", DisplayEntry.OperationsGroup, "alarm", Short: "Alarms"),
        new("Events", "events", DisplayEntry.OperationsGroup, "events", Short: "Events"),
        new("Incidents", "incidents", DisplayEntry.OperationsGroup, "incident", Short: "Incidents"),
        new("Scenarios", "grid/scenarios", DisplayEntry.OperationsGroup, "scenario", Short: "Scenarios"),
        new("System health", "health", DisplayEntry.SystemGroup, "health", Short: "Health"),
        new("My account", "identity", DisplayEntry.SystemGroup, "account", Short: "Account"),
        new("Grid instructor", "grid/instructor", DisplayEntry.AdministrationGroup, "instructor", AdminOnly: true, Short: "Trainer"),
        new("Network editor", "grid/editor", DisplayEntry.AdministrationGroup, "editor", AdminOnly: true, Short: "Editor"),
        new("Detection rules", "grid/rules", DisplayEntry.AdministrationGroup, "rules", AdminOnly: true, Short: "Rules"),
        new("Accounts & rules", "accounts", DisplayEntry.AdministrationGroup, "admin", AdminOnly: true, Short: "Admins"),
    ];
}
