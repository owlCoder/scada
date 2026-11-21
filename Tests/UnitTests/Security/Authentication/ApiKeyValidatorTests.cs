using Security.Authentication;
using Xunit;

namespace UnitTests.Security.Authentication;

public sealed class ApiKeyValidatorTests
{
    [Theory]
    [InlineData("", "value")]
    [InlineData("value", "")]
    [InlineData("value", "other")]
    [InlineData("value", "value ")]
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

    [Fact]
    public Task PasswordHashesVerifyAndAreSalted()
    {
        var first = PasswordHasher.Hash("correct horse");
        var second = PasswordHasher.Hash("correct horse");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("correct horse", first));
        Assert.False(PasswordHasher.Verify("wrong", first));
        Assert.False(PasswordHasher.Verify("correct horse", null));
        Assert.Equal(64, PasswordHasher.GenerateApiKey().Length);
        return Task.CompletedTask;
    }
}
