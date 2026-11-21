using Domain.Grid.Definition;

namespace Api.Endpoints.Grid;

public sealed record GridDefinitionSaveRequest(GridDefinition Definition, string Comment = "");
