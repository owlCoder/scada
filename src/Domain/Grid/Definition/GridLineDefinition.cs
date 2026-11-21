namespace Domain.Grid.Definition;

/// <summary>Overhead line or cable between two buses of the same nominal voltage. Impedances are per kilometre.</summary>
public sealed record GridLineDefinition(
    string Id,
    string Name,
    string FromBusId,
    string ToBusId,
    GridLineKind Kind,
    double LengthKm,
    double ResistanceOhmPerKm,
    double ReactanceOhmPerKm,
    double SusceptanceMicroSiemensPerKm,
    double RatedCurrentA,
    string? CatalogType,
    IReadOnlyList<DiagramPosition> Waypoints);
