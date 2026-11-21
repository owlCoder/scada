using Application.Identity;
using Security.Authentication;

namespace Api.Development;

public sealed class DevelopmentAccountGenerator(
    DevelopmentAccountSettings settings,
    IUserAccountStore accounts,
    IHostEnvironment environment,
    TimeProvider time,
    ILogger<DevelopmentAccountGenerator> logger)
{
    private const string Marker = "SCADA_DEV_";
    private static readonly (string Username, AccessRole Role)[] Accounts =
        [("dev-viewer", AccessRole.Viewer), ("dev-operator", AccessRole.Operator), ("dev-admin", AccessRole.Administrator)];

    /// <summary>Creates missing development accounts and returns how many were created. Does nothing outside Development.</summary>
    public async Task<int> EnsureAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !environment.IsDevelopment())
            return 0;

        var lines = new List<string>();
        foreach (var (username, role) in Accounts)
        {
            if (await accounts.FindAsync(username, cancellationToken) is not null)
                continue;

            var password = PasswordHasher.GeneratePassword();
            var apiKey = PasswordHasher.GenerateApiKey();
            var now = time.GetUtcNow();
            if (!await accounts.TryCreateAsync(new UserAccount(username, username, role, PasswordHasher.Hash(password),
                ApiKeyValidator.Fingerprint(apiKey), true, now, now, null, 0, null), cancellationToken))
                continue;
            var prefix = $"{Marker}{role.ToString().ToUpperInvariant()}";
            lines.AddRange([$"{prefix}_USERNAME={username}", $"{prefix}_PASSWORD={password}", $"{prefix}_API_KEY={apiKey}"]);
        }

        if (lines.Count == 0)
            return 0;

        var path = CredentialsPath();
        await WriteAsync(path, lines, cancellationToken);
        logger.LogInformation("Created {Count} development account(s); credentials written to {Path}", lines.Count / 3, path);
        return lines.Count / 3;
    }

    public string CredentialsPath()
    {
        if (!string.IsNullOrWhiteSpace(settings.CredentialsFile))
            return Path.GetFullPath(settings.CredentialsFile, environment.ContentRootPath);
        for (DirectoryInfo? directory = new(environment.ContentRootPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return Path.Combine(directory.FullName, ".scada", "dev-credentials.txt");
        }

        return Path.Combine(environment.ContentRootPath, ".scada", "dev-credentials.txt");
    }

    /// <summary>Replaces the lines of regenerated accounts and keeps everything else (for example compose secrets).</summary>
    private static async Task<bool> WriteAsync(string path, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var replaced = lines.Select(l => l[..(l.IndexOf('_', Marker.Length) + 1)]).ToHashSet(StringComparer.Ordinal);
        var existing = File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
        var kept = existing.Where(l => !replaced.Any(prefix => l.StartsWith(prefix, StringComparison.Ordinal)));
        // Owner-only permissions from the moment the file exists, not after the secrets are written.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (File.Exists(path))
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await using var writer = new StreamWriter(path, options);
        foreach (var line in kept.Concat(lines))
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        return true;
    }
}
