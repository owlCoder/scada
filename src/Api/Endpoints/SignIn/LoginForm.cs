namespace Api.Endpoints.SignIn;

public sealed record LoginForm(string? Username, string? Password, string? ReturnUrl);
