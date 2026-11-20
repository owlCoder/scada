using System.Text.RegularExpressions;
using Xunit;

namespace ArchitectureTests.Rules;

public sealed class SourceStructureTests
{
    private static readonly Regex TypeDeclaration = new(
        @"(?m)^\s*(?:public|internal|file)\s+(?:(?:sealed|static|abstract|partial|readonly|ref)\s+)*(?:class|interface|enum|struct|record(?:\s+class|\s+struct)?)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex VoidMethod = new(
        @"\bvoid\s+\w+\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public Task EachFileDeclaresExactlyOneMatchingType()
    {
        foreach (var file in SourceFiles())
        {
            var declarations = TypeDeclaration.Matches(File.ReadAllText(file));
            Assert.True(declarations.Count == 1, $"Expected one type in {file}; found {declarations.Count}.");
            Assert.Equal(Path.GetFileNameWithoutExtension(file), declarations[0].Groups[1].Value);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task ExplicitVoidMethodsAreNotAllowed()
    {
        foreach (var file in SourceFiles())
        {
            var match = VoidMethod.Match(File.ReadAllText(file));
            Assert.False(match.Success, $"Explicit void return type in {file}: {match.Value}");
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();
        foreach (var directory in new[] { "src", "Tests" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (parts.Contains("obj") || parts.Contains("bin"))
                    continue;

                yield return file;
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
