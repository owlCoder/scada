using Protocols.OpcUa;
using Xunit;

namespace UnitTests.Protocols;

public sealed class OpcUaOptionsTests
{
    [Fact]
    public Task DefaultsAreValidAndDisabled()
    {
        var options = new OpcUaOptions();
        Assert.False(options.Enabled);
        Assert.True(options.AllowAnonymous);
        Assert.True(options.AutoAcceptUntrustedCertificates);
        Assert.Equal(4840, options.Port);
        Assert.Empty(options.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0, "", "Plant")]
    [InlineData(1023, "store", " ")]
    [InlineData(65536, "bad\0path", "Plant")]
    public Task RejectsInvalidConfiguration(int port, string directory, string applicationName)
    {
        var errors = new OpcUaOptions { Port = port, PkiDirectory = directory, ApplicationName = applicationName }.Validate();
        Assert.Equal(2, errors.Count);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ResolvesPkiAgainstRepositoryOrContentRoot()
    {
        var root = Directory.CreateTempSubdirectory("scada-opcua-options-");
        try
        {
            var content = root.CreateSubdirectory("src").CreateSubdirectory("Api").FullName;
            var options = new OpcUaOptions();
            Assert.Equal(Path.Combine(content, ".scada", "pki"), options.ResolvePkiDirectory(content));
            File.WriteAllText(Path.Combine(root.FullName, "scada.slnx"), "");
            Assert.Equal(Path.Combine(root.FullName, ".scada", "pki"), options.ResolvePkiDirectory(content));
            options.PkiDirectory = "custom";
            Assert.Equal(Path.Combine(root.FullName, "custom"), options.ResolvePkiDirectory(content));
            options.PkiDirectory = Path.Combine(content, "absolute");
            Assert.Equal(options.PkiDirectory, options.ResolvePkiDirectory(content));
        }
        finally
        {
            root.Delete(recursive: true);
        }

        return Task.CompletedTask;
    }
}
