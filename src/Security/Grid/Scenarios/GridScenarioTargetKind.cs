namespace Security.Grid.Scenarios;

/// <summary>
/// Generic target selectors resolved against the running network when a scenario starts, so a scenario works on any
/// network the user drew ("the most loaded feeder breaker", "the transformer with an OLTC", "the busiest RTU").
/// </summary>
public enum GridScenarioTargetKind
{
    /// <summary>Breakers switching lines, ordered by current loading descending; <c>Count</c> of them.</summary>
    FeederBreakers,
    /// <summary>The line branches switched by feeder breakers, ordered by current loading descending; <c>Count</c> of them.</summary>
    FeederLines,
    /// <summary>The line branch with the highest current loading.</summary>
    MostLoadedFeeder,
    /// <summary>A protection relay whose switch is a feeder breaker.</summary>
    FeederProtection,
    /// <summary>The first transformer with an on-load tap changer.</summary>
    TransformerWithOltc,
    /// <summary>Loads ordered by peak demand descending; <c>Count</c> of them.</summary>
    LargestLoads,
    /// <summary>The RTU that controls the most loaded feeder.</summary>
    FeederRtu,
    /// <summary>A breaker controlled by <see cref="FeederRtu"/>.</summary>
    RtuBreaker,
    /// <summary>Any breaker.</summary>
    AnyBreaker,
    /// <summary>All capacitor banks.</summary>
    AllCapacitors,
    /// <summary>Current measurements of the most loaded feeder; <c>Count</c> of them.</summary>
    FeederCurrentMeasurements,
    /// <summary>The voltage measurement at the bus with the highest total load.</summary>
    HighestLoadBusVoltageMeasurement
}
