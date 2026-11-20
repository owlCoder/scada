using Security;
using Xunit;

namespace UnitTests.Security;

public sealed class ApiKeyValidatorTests
{
    [Theory]
    [InlineData("", "value")]
    [InlineData("value", "")]
    [InlineData("value", "other")]
    public Task RejectsInvalidCredentials(string expected, string supplied)
    {
        Assert.False(ApiKeyValidator.Matches(expected, supplied));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AcceptsMatchingCredentials()
    {
        Assert.True(ApiKeyValidator.Matches("strong-local-key", "strong-local-key"));
        return Task.CompletedTask;
    }
}
