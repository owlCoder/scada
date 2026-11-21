using System.Diagnostics;
using System.Text.RegularExpressions;
using Application.Diagnostics;
using Serilog.Context;

namespace Api.Observability;

/// <summary>
/// Accepts a well-formed <c>X-Correlation-Id</c> from the caller or generates one, echoes it in the response, and attaches it
/// to log entries, the request trace and every event published while handling the request.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = ValidId().IsMatch(supplied) ? supplied : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetBaggage(ScadaInstrumentation.CorrelationBaggageKey, correlationId);
        using (LogContext.PushProperty("CorrelationId", correlationId))
            await next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ValidId();
}
