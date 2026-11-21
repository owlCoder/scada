using Domain.Grid.Definition;

namespace Application.Grid;

public sealed record GridDefinitionVersion(
    int Version,
    GridDefinition Definition,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string Comment,
    bool Active);
