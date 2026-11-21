using Application.Grid;

namespace Api.Endpoints.Grid;

/// <summary>Body of <c>POST /api/grid/tags</c>.</summary>
public sealed record GridTagRequest(string ElementId, GridTagKind Kind, string Reason);
