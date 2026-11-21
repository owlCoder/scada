namespace Api.Observability;

public sealed class ObservabilitySettings
{
    public string ServiceName { get; set; } = "scada-api";

    /// <summary>Write logs as compact JSON (for log shippers) instead of human-readable text.</summary>
    public bool JsonLogs { get; set; }

    /// <summary>OTLP endpoint for traces and metrics, for example the Aspire dashboard (<c>http://aspire-dashboard:18889</c>). Empty disables export.</summary>
    public string? OtlpEndpoint { get; set; }

    public IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(OtlpEndpoint) || Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out _)
            ? []
            : ["Observability:OtlpEndpoint must be an absolute URL."];
}
