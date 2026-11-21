using Api.Observability;
using Application.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Api.Configuration.Registration;

/// <summary>
/// Serilog (levels from the <c>Serilog</c> section), OpenTelemetry tracing and metrics (ASP.NET Core, runtime, Npgsql and the
/// SCADA meters), OTLP export when <c>Observability:OtlpEndpoint</c> is set, and correlation ids.
/// </summary>
public static class ObservabilityRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaObservability(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.Bind<ObservabilitySettings>(ConfigurationSections.Observability);
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        var services = builder.Services;
        services.AddSingleton(settings);

        services.AddSerilog((provider, logger) =>
        {
            logger.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(provider)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", settings.ServiceName);
            if (settings.JsonLogs)
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            else
                logger.WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {CorrelationId}{NewLine}{Exception}");
        });

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(settings.ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddSource("Npgsql"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ScadaInstrumentation.MeterName, RuntimeMetrics.MeterName, "Npgsql"));
        if (!string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, new Uri(settings.OtlpEndpoint));

        services.AddSingleton<RuntimeMetrics>();
        return builder;
    }

    /// <summary>Correlation ids first, so every later log entry and event carries them; then one log line per request.</summary>
    public static WebApplication UseScadaObservability(this WebApplication app)
    {
        app.Services.GetRequiredService<RuntimeMetrics>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
                diagnostics.Set("User", context.User.Identity?.Name ?? "anonymous");
        });
        return app;
    }
}
