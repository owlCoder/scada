using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Api.Configuration.Registration;

public static class ForwardedHeadersRegistrationExtensions
{
    public static WebApplication UseScadaForwardedHeaders(this WebApplication app)
    {
        var configuredProxies = app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
        if (configuredProxies is not { Length: > 0 })
            return app;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in configuredProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain only IP addresses.");
            options.KnownProxies.Add(address);
        }

        app.UseForwardedHeaders(options);
        return app;
    }
}
