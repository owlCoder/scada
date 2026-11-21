using Application.Common;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class PagingTests
{
    [Theory]
    [InlineData("/api/events")]
    [InlineData("/api/incidents")]
    public async Task OversizedPageReturnsAnEmptyPageWithoutOverflow(string path)
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.ViewerKey);

        var page = await client.GetJsonAsync($"{path}?page={int.MaxValue}&pageSize={int.MaxValue}");

        Assert.Equal(Paging.MaxPage, page.GetProperty("page").GetInt32());
        Assert.Equal(Paging.MaxPageSize, page.GetProperty("pageSize").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }
}
