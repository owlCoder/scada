namespace Domain.Grid.Definition;

/// <summary>A node of the network. Voltage limits are per unit of <see cref="NominalVoltageKv"/>.</summary>
public sealed record GridBusDefinition(
    string Id,
    string Name,
    double NominalVoltageKv,
    GridBusKind Kind,
    DiagramPosition Position,
    double MinVoltagePu = 0.9,
    double MaxVoltagePu = 1.1);
