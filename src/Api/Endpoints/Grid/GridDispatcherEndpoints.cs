using Api.Authorization;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Api.Endpoints.Grid;

/// <summary>Dispatcher tag and trend endpoints: tag list/place/remove and the reported-measurement history.</summary>
public static class GridDispatcherEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/grid").RequireRole(AccessRole.Viewer);
        read.MapGet("/tags", (IGridTagging tagging) => Results.Ok(tagging.Tags));
        read.MapGet("/history/{measurementId}", (string measurementId, int? points, IGridHistory history) =>
            Results.Ok(history.Series(measurementId, points is { } p ? Math.Clamp(p, 0, GridHistoryService.Capacity) : DefaultPoints)));
        var write = app.MapGroup("/api/grid").RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        write.MapPost("/tags", async (GridTagRequest request, IGridTagging tagging, HttpContext http, CancellationToken ct) =>
            Result(await tagging.PlaceTagAsync(request.ElementId, request.Kind, request.Reason, http.Actor(), ct)));
        write.MapDelete("/tags/{id}", async (string id, IGridTagging tagging, HttpContext http, CancellationToken ct) =>
            Result(await tagging.RemoveTagAsync(id, http.Actor(), ct)));
        return app;
    }

    private const int DefaultPoints = 200;

    private static IResult Result(GridCommandResult result) => result.Accepted ? Results.Ok(result)
        : result.Reason == GridCommandRejection.NotAuthorized ? Results.Json(result, statusCode: StatusCodes.Status403Forbidden)
        : result.Reason == GridCommandRejection.RateLimited ? Results.Json(result, statusCode: StatusCodes.Status429TooManyRequests)
        : Results.BadRequest(result);
}
