using System.Threading.RateLimiting;
using Api.Authentication;
using Api.RateLimiting;

namespace Api.Configuration.Registration;

public static class RateLimitingRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaRateLimiting(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.Bind<RateLimitSettings>(ConfigurationSections.RateLimiting);
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton<RateLimitAuditor>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitions.For(context), _ => Window(settings.PermitsPerMinute)));
            options.AddPolicy(RateLimitSettings.LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientSource.Of(context), _ => Window(settings.LoginPermitsPerMinute)));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<RateLimitAuditor>()
                    .Record(RateLimitPartitions.For(context.HttpContext), context.HttpContext);
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
        });
        return builder;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
