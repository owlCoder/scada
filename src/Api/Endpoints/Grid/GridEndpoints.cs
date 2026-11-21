using Api.Authorization;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Definition;

namespace Api.Endpoints.Grid;

public static class GridEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/grid").RequireRole(AccessRole.Viewer);
        read.MapGet("", (IGridView view, HttpContext http) => Results.Ok(http.Actor().HasRole(AccessRole.Administrator)
            ? view.Snapshot() : GridViewRedactor.ForOperators(view.Snapshot(), view.Definition)));
        read.MapGet("/snapshot", (IGridView view, HttpContext http) => Results.Ok(http.Actor().HasRole(AccessRole.Administrator)
            ? view.Snapshot() : GridViewRedactor.ForOperators(view.Snapshot(), view.Definition)));
        read.MapGet("/status", (IGridView view) => Results.Ok(view.Status));
        read.MapGet("/definition", (IGridView view) => Results.Ok(view.Definition));
        read.MapGet("/definitions", async (IGridDefinitionStore store, CancellationToken ct) => Results.Ok(await store.ListAsync(ct)));
        read.MapGet("/measurements", (IGridView view, HttpContext http) => Results.Ok(http.Actor().HasRole(AccessRole.Administrator)
            ? view.Snapshot().Measurements : GridViewRedactor.ForOperators(view.Snapshot(), view.Definition).Measurements));
        var control = app.MapGroup("/api/grid").RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        control.MapPost("/commands", async (GridCommand command, IGridControl service, HttpContext http, CancellationToken ct) =>
            Result(await service.ExecuteAsync(command, http.Actor(), "rest", ct)));
        control.MapPost("/runtime/pause", async (IGridRuntime runtime, HttpContext http, CancellationToken ct) => Result(await runtime.SetPausedAsync(true, http.Actor(), ct)));
        control.MapPost("/runtime/resume", async (IGridRuntime runtime, HttpContext http, CancellationToken ct) => Result(await runtime.SetPausedAsync(false, http.Actor(), ct)));
        var admin = app.MapGroup("/api/grid").RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        admin.MapPost("/runtime/reset", async (IGridRuntime runtime, HttpContext http, CancellationToken ct) => Result(await runtime.ResetAsync(http.Actor(), ct)));
        admin.MapPost("/definitions/{version:int}/activate", async (int version, IGridRuntime runtime, HttpContext http, CancellationToken ct) =>
            Result(await runtime.ActivateVersionAsync(version, http.Actor(), ct)));
        admin.MapPost("/definitions/validate", (GridDefinition definition, IGridDefinitionAdministration service) => Results.Ok(service.Validate(definition)));
        admin.MapPost("/definitions", async (GridDefinitionSaveRequest request, IGridDefinitionAdministration service, HttpContext http, CancellationToken ct) =>
        {
            var saved = await service.SaveAsync(request.Definition, request.Comment, http.Actor(), ct);
            return saved.Saved ? Results.Ok(saved) : Results.BadRequest(saved);
        });
        admin.MapPost("/training", async (GridTrainingRequest request, IGridTraining service, HttpContext http, CancellationToken ct) =>
            Result(await Train(request, service, http.Actor(), ct)));
        return app;
    }

    private static Task<GridCommandResult> Train(GridTrainingRequest r, IGridTraining service, Actor actor, CancellationToken ct) => r.Action switch
    {
        "fault" => service.InjectFaultAsync(r.TargetId, r.Fault, actor, ct),
        "clearFault" => service.ClearFaultAsync(r.TargetId, actor, ct),
        "spoof" => service.SpoofMeasurementAsync(r.TargetId, r.Value, actor, ct),
        "protection" => service.ChangeProtectionAsync(r.TargetId, r.PickupValue ?? double.NaN, r.TimeSetting ?? double.NaN, r.Enabled, actor, ct),
        "switch" => service.ForceSwitchAsync(r.TargetId, r.Enabled, actor, ct),
        "tap" when r.Value is { } tap && double.IsFinite(tap) && tap == Math.Truncate(tap) && tap >= int.MinValue && tap <= int.MaxValue =>
            service.ForceTapAsync(r.TargetId, (int)tap, actor, ct),
        "rtu" => service.SetRtuOnlineAsync(r.TargetId, r.Enabled, actor, ct),
        "restore" => service.RestoreTrainingDefaultsAsync(actor, ct),
        _ => Task.FromResult(GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Unknown or invalid training action."))
    };

    private static IResult Result(GridCommandResult result) => result.Accepted ? Results.Ok(result)
        : result.Reason == GridCommandRejection.NotAuthorized ? Results.Json(result, statusCode: StatusCodes.Status403Forbidden)
        : result.Reason == GridCommandRejection.RateLimited ? Results.Json(result, statusCode: StatusCodes.Status429TooManyRequests)
        : Results.BadRequest(result);
}
