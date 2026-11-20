using Security;
using Xunit;

namespace UnitTests;

public sealed class ApiKeyValidatorTests
{
    [Theory]
    [InlineData("", "value")]
    [InlineData("value", "")]
    [InlineData("value", "other")]
    public void RejectsInvalidCredentials(string expected, string supplied)
    {
        Assert.False(ApiKeyValidator.Matches(expected, supplied));
    }

    [Fact]
    public void AcceptsMatchingCredentials()
    {
        Assert.True(ApiKeyValidator.Matches("strong-local-key", "strong-local-key"));
    }
}
