using Domain.Incidents;
using Domain.Process.Commands;
using Security.Accounts;
using Security.Scenarios;

namespace Api.Endpoints;

/// <summary>Maps service outcomes to HTTP responses consistently across endpoints.</summary>
public static class ResultMapping
{
    public static IResult ToHttp(this CommandResult result) => result.Accepted
        ? Results.Ok(result)
        : result.Reason switch
        {
            CommandRejectionReason.UnknownDevice => Results.NotFound(result),
            CommandRejectionReason.InvalidValue => Results.BadRequest(result),
            CommandRejectionReason.NotAuthorized => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            CommandRejectionReason.RateLimited => Results.Json(result, statusCode: StatusCodes.Status429TooManyRequests),
            CommandRejectionReason.CommunicationUnavailable => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Conflict(result)
        };

    public static IResult ToHttp(this IncidentChange change) => change.Status switch
    {
        IncidentOperationStatus.Succeeded => Results.Ok(change),
        IncidentOperationStatus.NotFound => Results.NotFound(change),
        IncidentOperationStatus.InvalidInput => Results.BadRequest(change),
        _ => Results.Conflict(change)
    };

    public static IResult ToHttp(this AccountOperationResult result) => result.Status switch
    {
        AccountOperationStatus.Succeeded => Results.Ok(result),
        AccountOperationStatus.NotFound => Results.NotFound(result),
        AccountOperationStatus.Denied => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
        AccountOperationStatus.Conflict => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };

    public static IResult ToHttp(this ScenarioControlResult result) =>
        result.Succeeded ? Results.Ok(result) : Results.Conflict(result);
}
