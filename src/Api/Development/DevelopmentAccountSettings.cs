namespace Api.Development;

/// <summary>
/// Development-only accounts (dev-viewer, dev-operator, dev-admin). When enabled in the Development environment, missing
/// accounts are created with random credentials that are written to <see cref="CredentialsFile"/> (default:
/// <c>.scada/dev-credentials.txt</c> at the repository root, which is gitignored). Secrets are never logged.
/// </summary>
public sealed class DevelopmentAccountSettings
{
    public bool Enabled { get; set; }
    public string? CredentialsFile { get; set; }
}
