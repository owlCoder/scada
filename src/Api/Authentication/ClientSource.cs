namespace Api.Authentication;

public static class ClientSource
{
    /// <summary>The audit/throttling key for the calling client (its remote IP address).</summary>
    public static string Of(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
