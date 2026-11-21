using Microsoft.AspNetCore.Routing.Matching;

namespace Api.Endpoints.System;

/// <summary>Lets the browser open the HMI at /health while existing probes keep their JSON response.</summary>
public sealed class HealthFormatPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public const string JsonEndpoint = "HealthJson";

    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => endpoints.Any(IsHealthJson);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        var acceptsHtml = httpContext.Request.GetTypedHeaders().Accept
            .Any(media => (media.Quality ?? 1) > 0 && media.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
        if (acceptsHtml)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (IsHealthJson(candidates[i].Endpoint))
                    candidates.SetValidity(i, false);
            }
        }
        return Task.CompletedTask;
    }

    private static bool IsHealthJson(Endpoint endpoint) => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == JsonEndpoint;
}
