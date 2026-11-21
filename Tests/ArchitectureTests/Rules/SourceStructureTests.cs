using System.Text.RegularExpressions;
using Xunit;

namespace ArchitectureTests.Rules;

public sealed class SourceStructureTests
{
    private static readonly Regex TypeDeclaration = new(
        @"(?m)^\s*(?:public|internal|file)\s+(?:(?:sealed|static|abstract|partial|readonly|ref)\s+)*(?:class|interface|enum|struct|record(?:\s+class|\s+struct)?)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex VoidMethod = new(@"\bvoid\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex Namespace = new(@"(?m)^namespace\s+([\w.]+)\s*;", RegexOptions.Compiled);
    private static readonly Regex RazorCode = new(@"@(?:code|functions)\s*\{", RegexOptions.Compiled);

    [Fact]
    public Task EachFileDeclaresExactlyOneMatchingType()
    {
        foreach (var file in SourceFiles("*.cs"))
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
        foreach (var file in SourceFiles("*.cs"))
        {
            var match = VoidMethod.Match(File.ReadAllText(file));
            Assert.False(match.Success, $"Explicit void return type in {file}: {match.Value}");
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task RazorCodeBlocksDoNotDeclareVoidMethods()
    {
        foreach (var file in SourceFiles("*.razor"))
        {
            var text = File.ReadAllText(file);
            foreach (Match block in RazorCode.Matches(text))
            {
                var code = text[block.Index..];
                var match = VoidMethod.Match(code);
                Assert.False(match.Success, $"Explicit void return type in @code block of {file}: {match.Value}");
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task NamespacesMatchFolders()
    {
        var root = RepositoryRoot();
        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);
            var match = Namespace.Match(text);
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Split(Path.DirectorySeparatorChar);
            if (!match.Success)
            {
                Assert.True(Path.GetFileName(file) == "Program.cs", $"{file} has no file-scoped namespace.");
                continue;
            }

            var expected = string.Join('.', relative.Skip(1));
            Assert.True(match.Groups[1].Value == expected, $"{file}: namespace {match.Groups[1].Value} should be {expected}.");
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task TestProjectsAreOrganisedByArea()
    {
        var root = RepositoryRoot();
        foreach (var file in SourceFiles("*.cs").Where(f => Path.GetRelativePath(root, f).StartsWith("Tests", StringComparison.Ordinal)))
        {
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            Assert.True(parts.Length >= 4, $"{file} must live in an area folder inside its test project (Tests/<Project>/<Area>/...).");

            var name = Path.GetFileNameWithoutExtension(file);
            var text = File.ReadAllText(file);
            var hasTests = text.Contains("[Fact]", StringComparison.Ordinal) || text.Contains("[Theory]", StringComparison.Ordinal);
            if (hasTests)
                Assert.True(name.EndsWith("Tests", StringComparison.Ordinal), $"{file}: test classes must end with 'Tests'.");
            else
                Assert.True(parts.Contains("Support"), $"{file}: classes without tests belong in a Support folder.");
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var root = RepositoryRoot();
        foreach (var directory in new[] { "src", "Tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), pattern, SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (parts.Contains("obj") || parts.Contains("bin") || parts.Contains("node_modules"))
                    continue;

                yield return file;
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
