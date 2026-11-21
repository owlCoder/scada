namespace DatabaseTests.Support;

public static class RepositoryFiles
{
    public static string Read(params string[] relativePath)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return File.ReadAllText(Path.Combine([directory.FullName, .. relativePath]));
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
