using Application.Grid;

namespace Security.Grid.Detection.Rules;

/// <summary>Creates the built-in grid detection rules. Rules that need the network definition read it from <see cref="IGridView"/>.</summary>
public static class DefaultGridDetectionRules
{
    public static IGridDetectionRule[] Create(IGridView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return
        [
            new GridUnsolicitedSwitchingRule(),
            new GridMassSwitchingRule(),
            new GridMeasurementInconsistencyRule(view),
            new GridProtectionTamperingRule(view),
            new GridTapManipulationRule(view),
            new GridVoltageViolationRule(view),
            new GridOverloadRule(),
            new GridRtuCommunicationLossRule(),
            new GridUnauthorizedCommandRule(),
            new GridLoadSheddingAnomalyRule()
        ];
    }
}
