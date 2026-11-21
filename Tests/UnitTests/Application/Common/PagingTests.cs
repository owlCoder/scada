using Application.Common;
using Xunit;

namespace UnitTests.Application.Common;

public sealed class PagingTests
{
    [Fact]
    public Task NormalizingTheLargestInputsKeepsOffsetInRange()
    {
        var (page, size) = Paging.Normalize(int.MaxValue, int.MaxValue);

        Assert.Equal(Paging.MaxPage, page);
        Assert.Equal(Paging.MaxPageSize, size);
        Assert.Equal(199_999_800, checked((page - 1) * size));
        Assert.Equal((1, 1), Paging.Normalize(int.MinValue, int.MinValue));
        return Task.CompletedTask;
    }
}
