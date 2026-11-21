using Domain.Grid.Definition;

namespace Hmi.Services.Grid;

public sealed record GridDiagramNode(string Id, string Name, string Kind, DiagramPosition Position);
