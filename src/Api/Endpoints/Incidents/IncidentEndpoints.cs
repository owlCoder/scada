using Api.Authorization;
using Api.Contracts;
using Application.Identity;
using Application.Incidents;
using Domain.Incidents;

namespace Api.Endpoints.Incidents;

public static class IncidentEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/incidents").RequireRole(AccessRole.Viewer);
        read.MapGet("", async (IncidentStatus? status, IncidentSeverity? severity, IncidentCategory? category, string? search,
                string? assignedTo, string? deviceId, Guid? scenarioRunId, bool? activeOnly, IncidentSortField? sortBy,
                bool? descending, int? page, int? pageSize, IncidentService incidents, CancellationToken cancellationToken) =>
            Results.Ok(await incidents.QueryAsync(new IncidentQuery(status, severity, category, search, assignedTo, deviceId,
                scenarioRunId, activeOnly ?? false, sortBy ?? IncidentSortField.CreatedAt, descending ?? true, page ?? 1,
                pageSize ?? 25), cancellationToken)));
        read.MapGet("/{id:guid}", async (Guid id, IncidentService incidents, CancellationToken cancellationToken) =>
            await incidents.GetDetailsAsync(id, cancellationToken) is { } details ? Results.Ok(details) : Results.NotFound());
        read.MapGet("/{id:guid}/timeline", async (Guid id, IncidentService incidents, CancellationToken cancellationToken) =>
            await incidents.GetDetailsAsync(id, cancellationToken) is { } details ? Results.Ok(details.Timeline) : Results.NotFound());

        var write = app.MapGroup("/api/incidents/{id:guid}").RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        write.MapPost("/status", async (Guid id, IncidentStatusRequest request, IncidentService incidents, HttpContext http,
                CancellationToken cancellationToken) =>
            !Enum.IsDefined(request.Status)
                ? Results.BadRequest("Invalid incident status.")
                : (await incidents.ChangeStatusAsync(id, request.Status, request.Notes, request.ExpectedVersion, http.Actor(),
                    cancellationToken)).ToHttp());
        write.MapPost("/assign", async (Guid id, IncidentAssignRequest request, IncidentService incidents, HttpContext http,
                CancellationToken cancellationToken) =>
            (await incidents.AssignAsync(id, request.Assignee, request.ExpectedVersion, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/priority", async (Guid id, IncidentPriorityRequest request, IncidentService incidents, HttpContext http,
                CancellationToken cancellationToken) =>
            !Enum.IsDefined(request.Priority)
                ? Results.BadRequest("Invalid priority.")
                : (await incidents.ChangePriorityAsync(id, request.Priority, request.ExpectedVersion, http.Actor(), cancellationToken)).ToHttp());
        write.MapPost("/comments", async (Guid id, IncidentCommentRequest request, IncidentService incidents, HttpContext http,
                CancellationToken cancellationToken) =>
            (await incidents.AddCommentAsync(id, request.Text ?? "", http.Actor(), cancellationToken)).ToHttp());
        return app;
    }
}
