using Application.Commands;
using Application.Identity;
using Domain.Control;
using Domain.Incidents;
using Security.Scenarios.Actions;

namespace Security.Scenarios.Catalog;

/// <summary>
/// The built-in training scenarios for the default plant, network and controller topology. Times are simulated
/// seconds. Every attack is simulated: it only manipulates the virtual environment or calls platform services.
/// </summary>
public static class DefaultScenarios
{
    private const string AttackerSource = "modbus:192.0.2.66";

    private static readonly Actor GuestViewer = new("guest-viewer", AccessRole.Viewer);
    private static readonly Actor OperatorAna = new("operator-ana", AccessRole.Operator);
    private static readonly Actor OperatorEva = new("operator-eva", AccessRole.Operator);
    private static readonly Actor OperatorFran = new("operator-fran", AccessRole.Operator);
    private static readonly Actor OperatorBob = new("operator-bob", AccessRole.Operator);
    private static readonly Actor OperatorCarl = new("operator-carl", AccessRole.Operator);
    private static readonly Actor FloodClient = new("flood-client", AccessRole.Operator);

    public static IReadOnlyList<ScenarioDefinition> Create() =>
    [
        UnauthorizedCommand(),
        SensorSpoofing(),
        RegisterTampering(),
        CommandReplay(),
        DenialOfService(),
        CommandFrequency(),
        RoleEscalation(),
        PlcCommunicationInterruption(),
        FalseMeasurementInjection(),
        ControllerStateMismatch(),
        UnexpectedActuatorActivation(),
        ProcessSafetyViolation(),
        DataIntegrityViolation(),
        OperatorMisuse()
    ];

    private static ScenarioDefinition UnauthorizedCommand() => new(
        "unauthorized-command", "Unauthorized control commands", nameof(IncidentCategory.Cybersecurity),
        "A viewer account repeatedly tries to start pump P-101 and move valve V-301.",
        30, ["repeated-rejected-commands", "unauthorized-access"],
        [
            At(2, new OperatorCommandAction(GuestViewer, OperatorCommandType.StartPump, "P-101")),
            At(6, new OperatorCommandAction(GuestViewer, OperatorCommandType.SetValvePosition, "V-301", 100)),
            At(10, new OperatorCommandAction(GuestViewer, OperatorCommandType.StartPump, "P-101")),
            At(14, new OperatorCommandAction(GuestViewer, OperatorCommandType.SetValvePosition, "V-301", 0))
        ]);

    private static ScenarioDefinition SensorSpoofing() => new(
        "sensor-spoofing", "Level sensor spoofing", nameof(IncidentCategory.Cybersecurity),
        "LT-102 reports a frozen 30 % while the real level in T-102 keeps changing.",
        120, ["mass-balance-inconsistency"],
        [At(2, new SpoofSensorAction("LT-102", 30))]);

    private static ScenarioDefinition RegisterTampering() => new(
        "register-tampering", "Controller register tampering", nameof(IncidentCategory.Cybersecurity),
        "A host on the enterprise LAN probes PLC-1, sends invalid Modbus writes and finally forces the level setpoints.",
        60, ["network-segmentation-violation", "invalid-register-operations", "controller-state-mismatch"],
        [
            At(1, new ConnectionAttemptAction("192.0.2.66", "ENT", "PLC-1", "CTL")),
            At(2, new RegisterWriteAction(1, 300, 1, null, AttackerSource)),
            At(4, new RegisterWriteAction(1, 5, 1, null, AttackerSource)),
            At(6, new RegisterWriteAction(1, 250, 1, null, AttackerSource)),
            At(10, new ForceRegisterAction("PLC-1", RegisterArea.Holding, ControllerRegisterMap.SetpointBase, 900)),
            At(10, new ForceRegisterAction("PLC-1", RegisterArea.Holding, ControllerRegisterMap.SetpointBase + 1, 980))
        ]);

    private static ScenarioDefinition CommandReplay() => new(
        "command-replay", "Command replay", nameof(IncidentCategory.Cybersecurity),
        "A captured, sequence-numbered start command for P-101 is replayed four times.",
        30, ["command-replay"],
        [
            At(2, new OperatorCommandAction(OperatorEva, OperatorCommandType.SetManual, "P-101", sequenced: true)),
            At(4, new OperatorCommandAction(OperatorEva, OperatorCommandType.StartPump, "P-101", captureAs: "start")),
            At(8, new OperatorCommandAction(OperatorEva, OperatorCommandType.StopPump, "P-101", sequenced: true)),
            At(12, new ReplayCommandAction(OperatorEva, OperatorCommandType.StartPump, "P-101", "start", 4)),
            At(20, new OperatorCommandAction(OperatorEva, OperatorCommandType.SetAutomatic, "P-101", sequenced: true))
        ],
        [RestoreAutomatic("P-101")]);

    private static ScenarioDefinition DenialOfService() => new(
        "denial-of-service", "Denial of service on the control network", nameof(IncidentCategory.Communication),
        "The SCADA-to-PLC-1 link and PLC-1 field bus are flooded while a client bursts control requests.",
        60, ["communication-failure", "excessive-control-requests"],
        [
            At(2, new LinkImpairmentAction("L-SCADA-PLC1", 400, 70)),
            At(3, new LinkImpairmentAction("L-PLC1-IO", 2, 80)),
            At(5, new CommandBurstAction(FloodClient, OperatorCommandType.SetValvePosition, "V-301", 15, 50))
        ]);

    private static ScenarioDefinition CommandFrequency() => new(
        "command-frequency", "Abnormal command frequency", nameof(IncidentCategory.Operational),
        "An operator oscillates outlet valve V-301 between 20 % and 80 % every two seconds.",
        40, ["excessive-control-requests", "conflicting-operator-commands"],
        [
            At(1, new OperatorCommandAction(OperatorAna, OperatorCommandType.SetManual, "V-301")),
            .. Enumerable.Range(0, 10).Select(i =>
                At(2 + 2 * i, new OperatorCommandAction(OperatorAna, OperatorCommandType.SetValvePosition, "V-301", i % 2 == 0 ? 20 : 80))),
            At(24, new OperatorCommandAction(OperatorAna, OperatorCommandType.SetAutomatic, "V-301"))
        ],
        [RestoreAutomatic("V-301")]);

    private static ScenarioDefinition RoleEscalation() => new(
        "role-escalation", "Privilege escalation attempt", nameof(IncidentCategory.Cybersecurity),
        "An operator tries to grant itself the Administrator role and to rotate the administrator API key.",
        30, ["unauthorized-access"],
        [
            At(2, new RoleChangeAttemptAction(OperatorBob, OperatorBob.Name, AccountAttemptKind.ChangeRole)),
            At(4, new RoleChangeAttemptAction(OperatorBob, OperatorBob.Name, AccountAttemptKind.ChangeRole)),
            At(6, new RoleChangeAttemptAction(OperatorBob, OperatorBob.Name, AccountAttemptKind.ChangeRole)),
            At(8, new RoleChangeAttemptAction(OperatorBob, "admin", AccountAttemptKind.RotateApiKey))
        ]);

    private static ScenarioDefinition PlcCommunicationInterruption() => new(
        "plc-communication-interruption", "PLC field communication interruption", nameof(IncidentCategory.Communication),
        "PLC-1 loses its remote I/O link, falls into safe state and is reset after the link returns.",
        60, ["communication-failure", "controller-state-mismatch"],
        [
            At(2, new LinkStateAction("L-PLC1-IO", false)),
            At(40, new LinkStateAction("L-PLC1-IO", true)),
            At(45, new ControllerControlAction("PLC-1", ControllerOperation.Reset))
        ]);

    private static ScenarioDefinition FalseMeasurementInjection() => new(
        "false-measurement-injection", "False measurement injection", nameof(IncidentCategory.Cybersecurity),
        "An operator opens inlet V-101 fully; FT-101 then reports zero although water is flowing, and PT-101 jumps to 8 bar.",
        90, ["impossible-measurement-change", "mass-balance-inconsistency"],
        [
            At(0, new OperatorCommandAction(OperatorFran, OperatorCommandType.SetManual, "V-101")),
            At(0.5, new OperatorCommandAction(OperatorFran, OperatorCommandType.SetValvePosition, "V-101", 100)),
            At(2, new SpoofSensorAction("FT-101", 0)),
            At(4, new SpoofSensorAction("PT-101", 8))
        ],
        [RestoreAutomatic("V-101")]);

    private static ScenarioDefinition ControllerStateMismatch() => new(
        "controller-state-mismatch", "Controller output mismatch", nameof(IncidentCategory.Cybersecurity),
        "Forced output registers make PLC-2 command P-102 on and PLC-1 hold a stale output.",
        40, ["controller-state-mismatch"],
        [
            At(2, new ForceRegisterAction("PLC-2", RegisterArea.Holding, 0, 1)),
            At(4, new ForceRegisterAction("PLC-1", RegisterArea.Holding, 2, 0))
        ]);

    private static ScenarioDefinition UnexpectedActuatorActivation() => new(
        "unexpected-actuator-activation", "Unexpected actuator activation", nameof(IncidentCategory.Cybersecurity),
        "Drain valve V-302 opens and standby pump P-103 starts without any operator or controller command.",
        60, ["unexpected-valve-change", "unexpected-pump-activity"],
        [
            At(2, new ForceActuatorAction("V-302", 100)),
            At(4, new ForceActuatorAction("P-103", 1)),
            At(35, new ForceActuatorAction("V-302", 0))
        ]);

    private static ScenarioDefinition ProcessSafetyViolation() => new(
        "process-safety-violation", "Process safety violation", nameof(IncidentCategory.ProcessSafety),
        "With T-102 near high-high and interlocks bypassed, the PLC-1 fill setpoints are forced to 95-100 %, PLC-2 is forced to stop draining T-102 and the transfer pumps are forced on.",
        90, ["process-safety-violation", "abnormal-tank-level", "unexpected-pump-activity"],
        [
            At(2, new SetTankLevelAction("T-102", 88)),
            At(4, new InterlockBypassAction(true, "unattributed")),
            At(6, new ForceRegisterAction("PLC-1", RegisterArea.Holding, ControllerRegisterMap.SetpointBase, 950)),
            At(6, new ForceRegisterAction("PLC-1", RegisterArea.Holding, ControllerRegisterMap.SetpointBase + 1, 1000)),
            At(6, new ForceRegisterAction("PLC-2", RegisterArea.Holding, ControllerRegisterMap.SetpointBase, 0)),
            At(6, new ForceRegisterAction("PLC-2", RegisterArea.Holding, ControllerRegisterMap.SetpointBase + 1, 10)),
            At(8, new ForceActuatorAction("P-101", 1)),
            At(8, new ForceActuatorAction("P-103", 1))
        ]);

    private static ScenarioDefinition DataIntegrityViolation() => new(
        "data-integrity-violation", "Historian data integrity violation", nameof(IncidentCategory.Cybersecurity),
        "Historian samples for LT-103 and FT-301 are silently rewritten.",
        30, ["historian-integrity"],
        [
            At(5, new HistorianTamperAction("LT-103", 12.5)),
            At(15, new HistorianTamperAction("FT-301", 0))
        ]);

    private static ScenarioDefinition OperatorMisuse() => new(
        "operator-misuse", "Operator misuse", nameof(IncidentCategory.Operational),
        "An operator puts several devices in manual and acknowledges every alarm without responding to it.",
        60, ["operator-misuse"],
        [
            At(2, new SetTankLevelAction("T-101", 90)),
            At(2, new SetTankLevelAction("T-103", 10)),
            .. ManualDevices.Select((device, i) =>
                At(4 + 2 * i, new OperatorCommandAction(OperatorCarl, OperatorCommandType.SetManual, device))),
            .. new[] { 12d, 24, 36, 48 }.Select(t => At(t, new AlarmAcknowledgeBurstAction(OperatorCarl))),
            .. ManualDevices.Select(device =>
                At(55, new OperatorCommandAction(OperatorCarl, OperatorCommandType.SetAutomatic, device)))
        ],
        [.. ManualDevices.Select(RestoreAutomatic)]);

    private static string[] ManualDevices => ["P-101", "P-102", "V-301", "V-102"];

    private static ScheduledScenarioAction At(double seconds, IScenarioAction action) => new(seconds, action);

    private static IScenarioAction RestoreAutomatic(string deviceId) => new RestoreAutomaticModeAction(deviceId);
}
