using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Filtering;
using Aggregator.Application.UnitTests.TestData;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class ItemFilterTests
{
    private static readonly DateTimeOffset At = TestItems.PublishedAt;
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    [Theory]
    [InlineData("news", "news", true)]
    [InlineData("news", "NEWS", true)]
    [InlineData("weather", "news", false)]
    public void CategoryFilter_ShouldMatchIgnoringCase_WhenCategoryIsGiven(string itemCategory, string category, bool expected)
    {
        var filter = new CategoryFilter(category);

        filter.Matches(TestItems.Item("a", category: itemCategory)).ShouldBe(expected);
    }

    public static TheoryData<string, DateTimeOffset?, DateTimeOffset?, bool> DateRanges => new()
    {
        { "from only, equal", At, null, true },
        { "from only, one tick later", At + Tick, null, false },
        { "to only, equal", null, At, true },
        { "to only, one tick earlier", null, At - Tick, false },
        { "from == to == published", At, At, true },
        { "inside both bounds", At.AddHours(-1), At.AddHours(1), true },
        { "after the range", At.AddHours(-2), At.AddHours(-1), false },
        { "before the range", At.AddHours(1), At.AddHours(2), false },
        { "same instant, other offset", At.ToOffset(TimeSpan.FromHours(2)), null, true },
    };

    [Theory]
    [MemberData(nameof(DateRanges))]
    public void DateRangeFilter_ShouldIncludeBothBounds_WhenRangeIsGiven(string scenario, DateTimeOffset? from, DateTimeOffset? to, bool expected)
    {
        var filter = new DateRangeFilter(from, to);

        filter.Matches(TestItems.Item("a", publishedAt: At)).ShouldBe(expected, scenario);
    }

    [Fact]
    public void DateRangeFilter_ShouldThrow_WhenFromIsAfterTo() =>
        Should.Throw<ArgumentException>(() => new DateRangeFilter(At + Tick, At));

    [Fact]
    public void For_ShouldReturnNoFilters_WhenQueryHasNoCategoryOrDates() =>
        ItemFilters.For(new AggregationQuery(1, 10)).ShouldBeEmpty();

    [Fact]
    public void For_ShouldReturnCategoryAndDateFilters_WhenQueryHasBoth()
    {
        var query = new AggregationQuery(1, 10) { Category = "news", From = At };

        var filters = ItemFilters.For(query);

        filters.Select(filter => filter.GetType()).ShouldBe([typeof(CategoryFilter), typeof(DateRangeFilter)]);
    }

    [Fact]
    public void Apply_ShouldKeepOnlyItemsMatchingEveryFilter_WhenFiltersAreCombined()
    {
        var query = new AggregationQuery(1, 10) { Category = "news", From = At };
        var items = new[]
        {
            TestItems.Item("keep", category: "news", publishedAt: At),
            TestItems.Item("wrong-category", category: "weather", publishedAt: At),
            TestItems.Item("too-early", category: "news", publishedAt: At - Tick),
        };

        var kept = ItemFilters.Apply(items, ItemFilters.For(query));

        kept.Select(item => item.Id).ShouldBe(["keep"]);
    }
}
