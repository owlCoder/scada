using Application.Common;

namespace Api.Endpoints.SignIn;

public sealed record LoginForm
{
    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? ReturnUrl { get; init; }

    public SimulationMode? Mode { get; init; }
}
