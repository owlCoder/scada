using Application.Common;

namespace Hmi.Services.Shell;

/// <summary>The mode-aware list of displays shown in the icon rail and the Ctrl+K quick switcher.</summary>
public static class DisplayCatalog
{
    public static IReadOnlyList<DisplayEntry> For(SimulationMode mode) => mode == SimulationMode.Grid ? Grid : Water;

    private static IReadOnlyList<DisplayEntry> Water { get; } =
    [
        new("Overview", "", DisplayEntry.ProcessGroup, "overview"),
        new("Plant diagram", "diagram", DisplayEntry.ProcessGroup, "diagram"),
        new("Tanks", "tanks", DisplayEntry.ProcessGroup, "tank"),
        new("Pumps", "pumps", DisplayEntry.ProcessGroup, "pump"),
        new("Valves", "valves", DisplayEntry.ProcessGroup, "valve"),
        new("Sensors", "sensors", DisplayEntry.ProcessGroup, "sensor"),
        new("PLC / RTU", "controllers", DisplayEntry.ProcessGroup, "controller"),
        new("Trends", "trends", DisplayEntry.ProcessGroup, "trend"),
        new("Alarms", "alarms", DisplayEntry.OperationsGroup, "alarm"),
        new("Events", "events", DisplayEntry.OperationsGroup, "events"),
        new("Incidents", "incidents", DisplayEntry.OperationsGroup, "incident"),
        new("Scenarios", "scenarios", DisplayEntry.OperationsGroup, "scenario"),
        new("Communications", "communications", DisplayEntry.SystemGroup, "comm"),
        new("Registers", "registers", DisplayEntry.SystemGroup, "register"),
        new("System health", "health", DisplayEntry.SystemGroup, "health"),
        new("My account", "identity", DisplayEntry.SystemGroup, "account"),
        new("Accounts & rules", "accounts", DisplayEntry.AdministrationGroup, "admin", AdminOnly: true),
    ];

    private static IReadOnlyList<DisplayEntry> Grid { get; } =
    [
        new("Grid overview", "grid", DisplayEntry.ProcessGroup, "overview"),
        new("Single-line diagram", "grid/diagram", DisplayEntry.ProcessGroup, "sld"),
        new("Feeders", "grid/feeders", DisplayEntry.ProcessGroup, "feeder"),
        new("Transformers", "grid/transformers", DisplayEntry.ProcessGroup, "transformer"),
        new("Switches", "grid/switches", DisplayEntry.ProcessGroup, "switch"),
        new("Loads", "grid/loads", DisplayEntry.ProcessGroup, "load"),
        new("Protection", "grid/protection", DisplayEntry.ProcessGroup, "protection"),
        new("Measurements", "grid/measurements", DisplayEntry.ProcessGroup, "sensor"),
        new("Grid alarms", "grid/alarms", DisplayEntry.OperationsGroup, "alarm"),
        new("Events", "events", DisplayEntry.OperationsGroup, "events"),
        new("Incidents", "incidents", DisplayEntry.OperationsGroup, "incident"),
        new("Scenarios", "grid/scenarios", DisplayEntry.OperationsGroup, "scenario"),
        new("System health", "health", DisplayEntry.SystemGroup, "health"),
        new("My account", "identity", DisplayEntry.SystemGroup, "account"),
        new("Grid instructor", "grid/instructor", DisplayEntry.AdministrationGroup, "instructor", AdminOnly: true),
        new("Network editor", "grid/editor", DisplayEntry.AdministrationGroup, "editor", AdminOnly: true),
        new("Detection rules", "grid/rules", DisplayEntry.AdministrationGroup, "rules", AdminOnly: true),
        new("Accounts & rules", "accounts", DisplayEntry.AdministrationGroup, "admin", AdminOnly: true),
    ];
}
