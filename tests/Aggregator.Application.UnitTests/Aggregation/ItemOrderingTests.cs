using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.UnitTests.TestData;
using Aggregator.Domain.Items;
using SortDirection = Aggregator.Application.Aggregation.Sorting.SortDirection;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class ItemOrderingTests
{
    private static readonly DateTimeOffset Noon = TestItems.PublishedAt;

    private static readonly AggregatedItem[] Mixed =
    [
        TestItems.Item("a", source: "news", publishedAt: Noon.AddHours(-2), relevance: 0.9),
        TestItems.Item("b", source: "weather", publishedAt: Noon, relevance: 0.2),
        TestItems.Item("c", source: "repositories", publishedAt: Noon.AddHours(-1), relevance: 0.5),
    ];

    public static TheoryData<ItemSortField, SortDirection, string[]> PrimaryKeys => new()
    {
        { ItemSortField.Relevance, SortDirection.Descending, ["a", "c", "b"] },
        { ItemSortField.Relevance, SortDirection.Ascending, ["b", "c", "a"] },
        { ItemSortField.Date, SortDirection.Descending, ["b", "c", "a"] },
        { ItemSortField.Date, SortDirection.Ascending, ["a", "c", "b"] },
        { ItemSortField.Source, SortDirection.Ascending, ["a", "c", "b"] },
        { ItemSortField.Source, SortDirection.Descending, ["b", "c", "a"] },
    };

    [Theory]
    [MemberData(nameof(PrimaryKeys))]
    public void Sort_ShouldOrderByField_WhenFieldAndDirectionAreGiven(ItemSortField field, SortDirection direction, string[] expected)
    {
        var sorted = ItemOrdering.Sort(Mixed, field, direction);

        sorted.Select(item => item.Id).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SortDirection.Descending)]
    [InlineData(SortDirection.Ascending)]
    public void Sort_ShouldPutNewerFirst_WhenRelevanceIsTied(SortDirection direction)
    {
        AggregatedItem[] tied = [TestItems.Item("old", publishedAt: Noon.AddHours(-1)), TestItems.Item("new", publishedAt: Noon)];

        var sorted = ItemOrdering.Sort(tied, ItemSortField.Relevance, direction);

        sorted.Select(item => item.Id).ShouldBe(["new", "old"]);
    }

    [Theory]
    [InlineData(ItemSortField.Date)]
    [InlineData(ItemSortField.Source)]
    public void Sort_ShouldPutMoreRelevantFirst_WhenPrimaryKeyIsTied(ItemSortField field)
    {
        AggregatedItem[] tied = [TestItems.Item("low", relevance: 0.1), TestItems.Item("high", relevance: 0.8)];

        var sorted = ItemOrdering.Sort(tied, field, SortDirection.Ascending);

        sorted.Select(item => item.Id).ShouldBe(["high", "low"]);
    }

    [Fact]
    public void Sort_ShouldOrderSourcesIgnoringCase_WhenSortingBySource()
    {
        AggregatedItem[] items = [TestItems.Item("b", source: "Weather"), TestItems.Item("a", source: "news")];

        var sorted = ItemOrdering.Sort(items, ItemSortField.Source, SortDirection.Ascending);

        sorted.Select(item => item.Id).ShouldBe(["a", "b"]);
    }

    [Theory]
    [InlineData(ItemSortField.Relevance)]
    [InlineData(ItemSortField.Date)]
    [InlineData(ItemSortField.Source)]
    public void Sort_ShouldFallBackToId_WhenEveryKeyIsTied(ItemSortField field)
    {
        AggregatedItem[] identical = [TestItems.Item("news-2"), TestItems.Item("news-10"), TestItems.Item("news-1")];

        var sorted = ItemOrdering.Sort(identical, field, SortDirection.Descending);

        sorted.Select(item => item.Id).ShouldBe(["news-1", "news-10", "news-2"]);
    }

    [Fact]
    public void Sort_ShouldThrow_WhenFieldIsUndefined() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ItemOrdering.Sort(Mixed, (ItemSortField)99, SortDirection.Ascending));
}
