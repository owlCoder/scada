namespace Protocols.OpcUa;

public sealed class OpcUaOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 4840;
    public bool AllowAnonymous { get; set; } = true;
    public string PkiDirectory { get; set; } = ".scada/pki";
    public string ApplicationName { get; set; } = "SCADA Training Plant OPC UA";
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Port is < 1024 or > 65535)
            errors.Add("OPC UA port must be between 1024 and 65535.");
        if (!IsValidPath(PkiDirectory))
            errors.Add("OPC UA PkiDirectory must be a valid directory path.");
        if (string.IsNullOrWhiteSpace(ApplicationName))
            errors.Add("OPC UA ApplicationName is required.");
        return errors;
    }

    public string ResolvePkiDirectory(string contentRoot)
    {
        for (DirectoryInfo? directory = new(contentRoot); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return Path.GetFullPath(PkiDirectory, directory.FullName);
        }

        return Path.GetFullPath(PkiDirectory, contentRoot);
    }

    private static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            return Path.GetFullPath(path).Length > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
