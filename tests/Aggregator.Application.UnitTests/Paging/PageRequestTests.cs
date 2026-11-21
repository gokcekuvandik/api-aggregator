using Aggregator.Application.Paging;

namespace Aggregator.Application.UnitTests.Paging;

/// <summary>Offset paging with a maximum page size.</summary>
public sealed class PageRequestTests
{
    private static readonly IReadOnlyList<int> FiveItems = [1, 2, 3, 4, 5];

    [Fact]
    public void Apply_ShouldReturnRequestedSlice_WhenPageIsInRange()
    {
        var page = PageRequest.Apply(FiveItems, page: 2, pageSize: 2);

        page.Items.ShouldBe([3, 4]);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public void Apply_ShouldReturnPartialPage_WhenLastPageIsNotFull()
    {
        var page = PageRequest.Apply(FiveItems, page: 3, pageSize: 2);

        page.Items.ShouldBe([5]);
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public void Apply_ShouldReturnNoItemsButTotalCount_WhenPageIsPastTheEnd()
    {
        var page = PageRequest.Apply(FiveItems, page: 4, pageSize: 2);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public void Apply_ShouldReturnEmptyPage_WhenThereAreNoItems()
    {
        var page = PageRequest.Apply<int>([], page: 1, pageSize: 10);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PageRequest.MaxPageSize)]
    public void Apply_ShouldAcceptPageSize_WhenOnTheBoundary(int pageSize)
    {
        var items = Enumerable.Range(1, PageRequest.MaxPageSize + 1).ToList();

        var page = PageRequest.Apply(items, page: 1, pageSize);

        page.Items.Count.ShouldBe(pageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PageRequest.MaxPageSize + 1)]
    public void Apply_ShouldThrow_WhenPageSizeIsOutOfRange(int pageSize) =>
        Should.Throw<ArgumentOutOfRangeException>(() => PageRequest.Apply(FiveItems, page: 1, pageSize));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_ShouldThrow_WhenPageIsLessThanOne(int page) =>
        Should.Throw<ArgumentOutOfRangeException>(() => PageRequest.Apply(FiveItems, page, pageSize: 2));

    [Fact]
    public void DefaultPageSize_ShouldBeWithinAllowedRange() =>
        PageRequest.DefaultPageSize.ShouldBeInRange(1, PageRequest.MaxPageSize);
}
