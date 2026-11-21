namespace Domain.Grid.Definition;

/// <summary>
/// Complete description of an electrical distribution network: topology, equipment ratings, protection, RTUs and the
/// single-line diagram layout. Voltages are kV, powers MW / Mvar / MVA, currents A. This record is what the editor saves
/// and what the simulation loads.
/// </summary>
public sealed record GridDefinition(
    string Name,
    double NominalFrequencyHz,
    IReadOnlyList<GridBusDefinition> Buses,
    IReadOnlyList<GridSourceDefinition> Sources,
    IReadOnlyList<GridLineDefinition> Lines,
    IReadOnlyList<GridTransformerDefinition> Transformers,
    IReadOnlyList<GridSwitchDefinition> Switches,
    IReadOnlyList<GridLoadDefinition> Loads,
    IReadOnlyList<GridGeneratorDefinition> Generators,
    IReadOnlyList<GridCapacitorDefinition> Capacitors,
    IReadOnlyList<GridProtectionDefinition> Protections,
    IReadOnlyList<GridRtuDefinition> Rtus);
