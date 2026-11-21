using Application.Common;
using Xunit;

namespace UnitTests.Application.Common;

public sealed class PagingTests
{
    [Fact]
    public Task QueryFiltersAreTrimmedAndBoundedOnTheServer()
    {
        var oversized = "  " + new string('x', 201) + "  ";
        var events = new global::Application.Events.EventQuery(Search: oversized, Actor: "  eva  ", Category: "  ").Normalize();
        var incidents = new global::Application.Incidents.IncidentQuery(Search: oversized, AssignedTo: "  eva  ").Normalize();
        Assert.Equal(new string('x', 200), events.Search);
        Assert.Equal(events.Search, incidents.Search);
        Assert.Equal("eva", events.Actor);
        Assert.Equal("eva", incidents.AssignedTo);
        Assert.Null(events.Category);
        return Task.CompletedTask;
    }

    [Fact]
    public Task NormalizingTheLargestInputsKeepsOffsetInRange()
    {
        var (page, size) = Paging.Normalize(int.MaxValue, int.MaxValue);

        Assert.Equal(Paging.MaxPage, page);
        Assert.Equal(Paging.MaxPageSize, size);
        Assert.Equal(1_999_800, checked((page - 1) * size));
        Assert.Equal((1, 1), Paging.Normalize(int.MinValue, int.MinValue));
        return Task.CompletedTask;
    }
}
