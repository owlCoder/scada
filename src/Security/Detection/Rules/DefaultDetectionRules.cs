using Application.Simulation;
using Application.Telemetry;

namespace Security.Detection.Rules;

public static class DefaultDetectionRules
{
    public static IReadOnlyList<IDetectionRule> Create(SimulationTopology topology, TelemetryHistorian historian) =>
    [
        new AbnormalTankLevelRule(),
        new MassBalanceRule(topology.Plant),
        new ImpossibleMeasurementChangeRule(),
        new UnexpectedPumpActivityRule(),
        new UnexpectedValveChangeRule(),
        new RepeatedRejectedCommandsRule(),
        new ExcessiveControlRequestsRule(),
        new InvalidRegisterOperationsRule(),
        new CommunicationFailureRule(),
        new ControllerStateMismatchRule(topology.Controllers),
        new ConflictingOperatorCommandsRule(),
        new UnauthorizedAccessRule(),
        new ReplayAttackRule(),
        new NetworkSegmentationRule(),
        new HistorianIntegrityRule(historian),
        new SafetyViolationRule(),
        new OperatorMisuseRule()
    ];
}
