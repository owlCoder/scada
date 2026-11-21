using Domain.Grid.Definition;

namespace Hmi.Services.Grid;

public sealed record GridConductorCatalogItem(string Name, GridLineKind Kind, double R, double X, double B, double RatedCurrentA);
