using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Sources;

namespace Aggregator.Application.UnitTests.Sources;

public sealed class SourceCacheKeyTests
{
    private const string Name = "weather";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly AggregationQuery Query = new(Page: 1, PageSize: 20);

    [Fact]
    public void For_ShouldIgnoreCaseCategorySortAndPaging_WhenBuilt()
    {
        var query = Query with { Text = "DotNet", City = "Athens" };
        var other = new AggregationQuery(Page: 3, PageSize: 5)
        {
            Text = "dotnet",
            City = "ATHENS",
            Category = "weather",
            SortBy = ItemSortField.Date,
            SortOrder = Application.Aggregation.Sorting.SortDirection.Ascending,
        };

        SourceCacheKey.For("Weather", SearchFields.All, query).ShouldBe(SourceCacheKey.For("weather", SearchFields.All, other));
    }

    [Fact]
    public void For_ShouldDiffer_WhenSearchTextContainsTheSeparator() =>
        SourceCacheKey.For(Name, SearchFields.All, Query with { Text = "a|city=b" })
            .ShouldNotBe(SourceCacheKey.For(Name, SearchFields.All, Query with { Text = "a", City = "b|city=" }));

    [Fact]
    public void For_ShouldDiffer_WhenUsedSearchFieldsDiffer()
    {
        var keys = new[]
        {
            SourceCacheKey.For(Name, SearchFields.All, Query),
            SourceCacheKey.For("news", SearchFields.All, Query),
            SourceCacheKey.For(Name, SearchFields.All, Query with { Text = "a" }),
            SourceCacheKey.For(Name, SearchFields.All, Query with { City = "a" }),
            SourceCacheKey.For(Name, SearchFields.All, Query with { From = Now }),
            SourceCacheKey.For(Name, SearchFields.All, Query with { To = Now }),
        };

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Length);
    }

    public static TheoryData<SearchFields> EachField => [SearchFields.Text, SearchFields.City, SearchFields.From, SearchFields.To];

    [Theory]
    [MemberData(nameof(EachField))]
    public void For_ShouldIgnoreField_WhenSourceDoesNotUseIt(SearchFields ignored)
    {
        var used = SearchFields.All & ~ignored;
        var changed = ignored switch
        {
            SearchFields.Text => Query with { Text = "dotnet" },
            SearchFields.City => Query with { City = "Athens" },
            SearchFields.From => Query with { From = Now },
            _ => Query with { To = Now },
        };

        SourceCacheKey.For(Name, used, changed).ShouldBe(SourceCacheKey.For(Name, used, Query));
    }

    [Fact]
    public void For_ShouldListOnlyUsedFieldsInFixedOrder_WhenBuilt()
    {
        var query = Query with { Text = "Dot Net", City = "Athens", From = Now };

        SourceCacheKey.For("repositories", SearchFields.Text, query).ShouldBe("source:repositories:v1:q=dot%20net");
        SourceCacheKey.For(Name, SearchFields.City | SearchFields.From, query).ShouldBe("source:weather:v1:city=athens|from=2026-11-21t12:00:00.0000000+00:00");
        SourceCacheKey.For(Name, SearchFields.None, query).ShouldBe("source:weather:v1:");
    }

    [Fact]
    public void For_ShouldHashTheSearch_WhenTheKeyWouldBeTooLong()
    {
        // Text and City are capped at 200 and 100 characters by the API, and every character may escape to three.
        var query = Query with { Text = new string('|', 200), City = new string('|', 100), From = Now, To = Now };

        var key = SourceCacheKey.For(new string('n', 50), SearchFields.All, query);
        var other = SourceCacheKey.For(new string('n', 50), SearchFields.All, query with { Text = new string('|', 199) });

        key.Length.ShouldBeLessThanOrEqualTo(SourceCacheKey.MaxLength);
        key.ShouldStartWith($"source:{new string('n', 50)}:v1:sha256:");
        key.ShouldNotBe(other);
    }

    [Fact]
    public void For_ShouldKeepTheSearchReadable_WhenTheKeyIsExactlyTheMaximumLength()
    {
        var text = TextForKeyOfLength(SourceCacheKey.MaxLength);

        SourceCacheKey.For(Name, SearchFields.Text, Query with { Text = text }).ShouldBe(TextKeyPrefix + text);
    }

    [Fact]
    public void For_ShouldHashTheSearch_WhenTheKeyIsOneOverTheMaximumLength() =>
        SourceCacheKey.For(Name, SearchFields.Text, Query with { Text = TextForKeyOfLength(SourceCacheKey.MaxLength + 1) })
            .ShouldStartWith($"source:{Name}:v1:sha256:");

    [Fact]
    public void For_ShouldKeepTheSearchReadable_WhenTheKeyFitsTheLimit() =>
        SourceCacheKey.For(Name, SearchFields.Text, Query with { Text = new string('a', 200) }).ShouldEndWith($"q={new string('a', 200)}");

    private const string TextKeyPrefix = $"source:{Name}:v1:q=";

    private static string TextForKeyOfLength(int keyLength) => new('a', keyLength - TextKeyPrefix.Length);
}
