using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Grid.Scenarios;

namespace Security.Grid.Scenarios.Catalog;

/// <summary>
/// The built-in grid attack scenarios, mapped to real incidents (Ukraine 2015/2016, Industroyer). Every target is a
/// generic selector resolved at run start, so the catalog works on any network the user draws.
/// </summary>
public static class DefaultGridScenarios
{
    private const string Attacker = "grid-attacker";
    private const string RogueIec104 = "iec104-rogue";
    private const string Guest = "guest-viewer";
    private const string Instructor = "instructor";

    public static GridScenarioDefinition[] Create() =>
    [
        IndustroyerBreakerOpening(),
        OltcManipulation(),
        ProtectionTamperingThenFault(),
        FalseDataInjection(),
        LoadSheddingSpoofing(),
        RtuDenialOfService(),
        UnauthorizedCommandAttempts(),
        CapacitorManipulation()
    ];

    private static ScheduledGridScenarioAction At(double seconds, GridScenarioActionSpec spec) => new(seconds, spec);

    private static GridScenarioTarget Target(GridScenarioTargetKind kind, int count = 1) => new(kind, count);

    /// <summary>Industroyer (Ukraine 2016): a compromised client directly opens several feeder breakers in quick succession.</summary>
    private static GridScenarioDefinition IndustroyerBreakerOpening() => new(
        "grid-industroyer-breaker-opening", "Industroyer-style breaker opening", nameof(IncidentCategory.Cybersecurity),
        "Direct (forced) opening of several feeder breakers in quick succession, without select-before-operate — the " +
        "Industroyer payload that caused the Ukraine 2016 blackout.",
        30, ["grid-unsolicited-switching", "grid-mass-switching"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 0, Actor: Attacker)),
            At(3, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 1, Actor: Attacker)),
            At(4, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 2, Actor: Attacker))
        ],
        [
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 0, Flag: true, Actor: Instructor)),
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 1, Flag: true, Actor: Instructor)),
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.FeederBreakers, 3), 2, Flag: true, Actor: Instructor))
        ]);

    /// <summary>Forced OLTC taps to the limit cause overvoltage at consumers.</summary>
    private static GridScenarioDefinition OltcManipulation() => new(
        "grid-oltc-manipulation", "OLTC manipulation", nameof(IncidentCategory.Cybersecurity),
        "The on-load tap changer of the transformer is forced to its maximum tap repeatedly, driving the LV voltage " +
        "above limits at the consumer buses.",
        120, ["grid-tap-manipulation", "grid-voltage-violation"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.ForceTapLimit, Target(GridScenarioTargetKind.TransformerWithOltc), Flag: true, Actor: Attacker)),
            At(4, new GridScenarioActionSpec(GridScenarioActionKind.ForceTapLimit, Target(GridScenarioTargetKind.TransformerWithOltc), Flag: true, Actor: Attacker)),
            At(6, new GridScenarioActionSpec(GridScenarioActionKind.ForceTapLimit, Target(GridScenarioTargetKind.TransformerWithOltc), Flag: true, Actor: Attacker))
        ],
        [At(0, new GridScenarioActionSpec(GridScenarioActionKind.RestoreTap, Target(GridScenarioTargetKind.TransformerWithOltc), Actor: Instructor))]);

    /// <summary>Industroyer-style protection tampering followed by a fault: the relay is disabled, then a short circuit trips backup protection.</summary>
    private static GridScenarioDefinition ProtectionTamperingThenFault() => new(
        "grid-protection-tampering-fault", "Protection tampering then fault", nameof(IncidentCategory.Cybersecurity),
        "A feeder relay is disabled and its pickup raised (Industroyer's protection tampering), then a three-phase short " +
        "circuit on the feeder makes backup protection trip several breakers — more customers lost than the fault alone.",
        60, ["grid-protection-tampering", "grid-mass-switching"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.ChangeProtection, Target(GridScenarioTargetKind.FeederProtection),
                Value: 99999, SecondaryValue: 99, Flag: false, Actor: Attacker)),
            At(10, new GridScenarioActionSpec(GridScenarioActionKind.InjectFault, Target(GridScenarioTargetKind.MostLoadedFeeder),
                Fault: GridEquipmentFault.ThreePhaseShortCircuit, Actor: Attacker))
        ],
        [
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.ClearFault, Target(GridScenarioTargetKind.MostLoadedFeeder), Actor: Instructor)),
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.RestoreProtection, Target(GridScenarioTargetKind.FeederProtection), Actor: Instructor))
        ]);

    /// <summary>False data injection: feeder current and bus voltage measurements are spoofed to hide an overload while load rises.</summary>
    private static GridScenarioDefinition FalseDataInjection() => new(
        "grid-false-data-injection", "False data injection", nameof(IncidentCategory.Cybersecurity),
        "Feeder current measurements are spoofed to a low value and the bus voltage measurement is shifted, so the " +
        "overload on the feeder is invisible in the control centre (reported values no longer balance — Ukraine 2015-style FDI).",
        90, ["grid-measurement-inconsistency"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.SpoofMeasurement, Target(GridScenarioTargetKind.FeederCurrentMeasurements), 0, Value: 15, Actor: Attacker)),
            At(4, new GridScenarioActionSpec(GridScenarioActionKind.SpoofMeasurement, Target(GridScenarioTargetKind.FeederCurrentMeasurements), 0, Value: 12, Actor: Attacker)),
            At(6, new GridScenarioActionSpec(GridScenarioActionKind.SpoofMeasurement, Target(GridScenarioTargetKind.HighestLoadBusVoltageMeasurement), Value: 10.5, Actor: Attacker))
        ],
        [
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.SpoofMeasurement, Target(GridScenarioTargetKind.FeederCurrentMeasurements), 0, Value: null, Actor: Instructor)),
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.SpoofMeasurement, Target(GridScenarioTargetKind.HighestLoadBusVoltageMeasurement), Value: null, Actor: Instructor))
        ]);

    /// <summary>Ukraine 2015-style load-shedding command spoofing: many loads shed by commands from an unexpected iec104 actor.</summary>
    private static GridScenarioDefinition LoadSheddingSpoofing() => new(
        "grid-load-shedding-spoofing", "Load-shedding command spoofing", nameof(IncidentCategory.Cybersecurity),
        "Shed-load commands for the largest loads arrive over the iec104 channel from an actor no operator console would " +
        "use — the Ukraine 2015 BlackEnergy attack shed power to hundreds of thousands of customers.",
        60, ["grid-load-shedding-anomaly", "grid-unauthorized-command"],
        Enumerable.Range(0, 8).Select(i => At(2 + 2.0 * i, new GridScenarioActionSpec(GridScenarioActionKind.Command,
                Target(GridScenarioTargetKind.LargestLoads, 8), i, Command: GridCommandType.ShedLoad,
                Actor: RogueIec104, Source: "iec104"))).ToArray(),
        Enumerable.Range(0, 8).Select(i => At(0, new GridScenarioActionSpec(GridScenarioActionKind.Command,
            Target(GridScenarioTargetKind.LargestLoads, 8), i, Command: GridCommandType.RestoreLoad,
            Actor: Instructor, Source: "hmi"))).ToArray());

    /// <summary>RTU denial of service: feeder RTU taken offline, then a breaker is operated during the blind period.</summary>
    private static GridScenarioDefinition RtuDenialOfService() => new(
        "grid-rtu-denial-of-service", "RTU denial of service", nameof(IncidentCategory.Cybersecurity),
        "The feeder RTU is taken offline (all its measurements go Bad), then a breaker on that RTU is opened during the " +
        "blind period while the control centre cannot see or acknowledge it.",
        60, ["grid-rtu-communication-loss", "grid-unsolicited-switching"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.SetRtuOnline, Target(GridScenarioTargetKind.FeederRtu), Flag: false, Actor: Attacker)),
            At(10, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.RtuBreaker), Actor: Attacker))
        ],
        [
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.SetRtuOnline, Target(GridScenarioTargetKind.FeederRtu), Flag: true, Actor: Instructor)),
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.ForceSwitch, Target(GridScenarioTargetKind.RtuBreaker), Flag: true, Actor: Instructor))
        ]);

    /// <summary>Viewer/unknown actor attempts and a replayed sequence-numbered command — both rejected by the platform.</summary>
    private static GridScenarioDefinition UnauthorizedCommandAttempts() => new(
        "grid-unauthorized-commands", "Unauthorized command attempts", nameof(IncidentCategory.Cybersecurity),
        "A viewer account tries to operate a breaker (NotAuthorized) and an operator replays a captured sequence-numbered " +
        "command (Replay). The platform rejects both; the attempts are detected.",
        40, ["grid-unauthorized-command"],
        [
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AnyBreaker),
                Command: GridCommandType.SelectOpen, Actor: Guest, Role: AccessRole.Viewer, Source: "rest")),
            At(3, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AnyBreaker),
                Command: GridCommandType.Execute, Actor: Guest, Role: AccessRole.Viewer, Source: "rest")),
            At(4, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AnyBreaker),
                Command: GridCommandType.SelectClose, Actor: Guest, Role: AccessRole.Viewer, Source: "rest")),
            At(8, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AnyBreaker),
                Command: GridCommandType.SelectOpen, Actor: Instructor, Source: "hmi", CaptureAs: "op")),
            At(10, new GridScenarioActionSpec(GridScenarioActionKind.ReplayCommand, Target(GridScenarioTargetKind.AnyBreaker),
                Command: GridCommandType.SelectOpen, Actor: Instructor, Source: "hmi", CaptureAs: "op"))
        ],
        [At(0, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AnyBreaker),
            Command: GridCommandType.Cancel, Actor: Instructor, Source: "hmi"))]);

    /// <summary>All capacitor steps switched in at once, causing overvoltage (no voltage regulation reacts).</summary>
    private static GridScenarioDefinition CapacitorManipulation() => new(
        "grid-capacitor-manipulation", "Capacitor bank manipulation", nameof(IncidentCategory.Cybersecurity),
        "All capacitor banks are switched to all steps in one burst from the iec104 channel, raising the voltage above " +
        "limits at light load.",
        120, ["grid-voltage-violation"],
        [
            At(1, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.TransformerWithOltc),
                Command: GridCommandType.SetTapManual, Actor: RogueIec104, Source: "iec104")),
            At(2, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AllCapacitors), -1,
                Value: -1, Command: GridCommandType.SetCapacitorSteps, Actor: RogueIec104, Source: "iec104"))
        ],
        [
            At(0, new GridScenarioActionSpec(GridScenarioActionKind.Command, Target(GridScenarioTargetKind.AllCapacitors), -1,
                Value: -2, Command: GridCommandType.SetCapacitorSteps, Actor: Instructor, Source: "hmi"))
        ]);
}
