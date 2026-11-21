using System.Text.RegularExpressions;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using Aggregator.Domain.Items;
using SortDirection = Aggregator.Application.Aggregation.Sorting.SortDirection;

namespace Aggregator.Application.UnitTests.Aggregation;

/// <summary>Runs every pairwise row through the service and checks it against an independent oracle.</summary>
public sealed partial class AggregationPairwiseTests
{
    private static readonly DateTimeOffset Base = TestItems.PublishedAt;
    private static readonly string[] SourceNames = ["news", "weather", "repositories"];

    [Theory]
    [MemberData(nameof(AggregationPairwiseCases.Data), MemberType = typeof(AggregationPairwiseCases))]
    public async Task AggregateAsync_ShouldSelectFilterAndSort_WhenRunWithPairwiseCase(PairwiseCase row)
    {
        var sources = SourceNames.Select(CreateSource).ToArray();
        var query = ToQuery(row);

        var result = await new AggregationService(new DataSourceRegistry(sources)).AggregateAsync(query, TestContext.Current.CancellationToken);

        var selected = SelectedSources(row);
        result.Sources.Select(s => s.Name).ShouldBe(selected, ignoreOrder: true);
        var expected = sources.Where(s => selected.Contains(s.Name)).SelectMany(s => ItemsOf(s.Name)).Where(item => Matches(item, query)).Select(item => item.Id);
        var actual = result.Items.Items;
        actual.Select(item => item.Id).ShouldBe(expected, ignoreOrder: true);
        foreach (var (earlier, later) in actual.Zip(actual.Skip(1)))
        {
            InOrder(earlier, later, query.SortBy, query.SortOrder).ShouldBeTrue($"{earlier.Id} before {later.Id}");
        }
    }

    [Fact]
    public void Rows_ShouldCoverEveryAllowedPair_WhenComparedWithModel()
    {
        var values = ModelValues();
        var allowedPairs = Combinations(values).Where(AggregationPairwiseCases.IsAllowed).SelectMany(Pairs).ToHashSet();
        var coveredPairs = AggregationPairwiseCases.Rows.SelectMany(row => Pairs(row.Values)).ToHashSet();

        AggregationPairwiseCases.Rows.ShouldAllBe(row => AggregationPairwiseCases.IsAllowed(row.Values));
        allowedPairs.Except(coveredPairs).ShouldBeEmpty();
    }

    [Fact]
    public void Model_ShouldDeclareSameParameters_WhenRowsWereGenerated()
    {
        var values = ModelValues();

        values.Keys.ShouldBe(AggregationPairwiseCases.Parameters);
        for (var i = 0; i < AggregationPairwiseCases.Parameters.Length; i++)
        {
            AggregationPairwiseCases.Rows.Select(row => row.Values[i]).Distinct().ShouldBe(values.Values.ElementAt(i), ignoreOrder: true);
        }
    }

    private static Dictionary<string, string[]> ModelValues() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", AggregationPairwiseCases.ModelFile))
            .Select(line => ParameterLine().Match(line))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries));

    private static IEnumerable<string[]> Combinations(Dictionary<string, string[]> values) =>
        values.Values.Aggregate(
            (IEnumerable<string[]>)[[]],
            (rows, options) => rows.SelectMany(row => options.Select(option => (string[])[.. row, option])));

    private static IEnumerable<string> Pairs(IReadOnlyList<string> row) =>
        from a in Enumerable.Range(0, row.Count)
        from b in Enumerable.Range(a + 1, row.Count - a - 1)
        select $"{a}={row[a]}|{b}={row[b]}";

    private static AggregationQuery ToQuery(PairwiseCase row)
    {
        var (from, to) = row.DateRange switch
        {
            "none" => ((DateTimeOffset?)null, (DateTimeOffset?)null),
            "fromOnly" => (Base.AddHours(-1), null),
            "toOnly" => (null, Base.AddHours(-1)),
            "both" => (Base.AddHours(-2), Base),
            "equal" => (Base.AddHours(-1), Base.AddHours(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.DateRange, "Unknown date range."),
        };

        return new AggregationQuery(Page: 1, PageSize: 100)
        {
            SortBy = Enum.Parse<ItemSortField>(row.SortBy, ignoreCase: true),
            SortOrder = row.SortOrder == "asc" ? SortDirection.Ascending : SortDirection.Descending,
            Category = row.Category == "none" ? null : row.Category,
            Sources = row.Sources == "all" ? [] : row.Sources.Split('+'),
            From = from,
            To = to,
        };
    }

    private static string[] SelectedSources(PairwiseCase row)
    {
        var named = row.Sources == "all" ? SourceNames : row.Sources.Split('+');
        return row.Category == "none" ? named : [.. named.Where(name => name == row.Category)];
    }

    private static bool Matches(AggregatedItem item, AggregationQuery query) =>
        (query.Category is null || item.Category == query.Category)
        && (query.From is null || item.PublishedAt >= query.From)
        && (query.To is null || item.PublishedAt <= query.To);

    private static bool InOrder(AggregatedItem earlier, AggregatedItem later, ItemSortField field, SortDirection direction)
    {
        var comparison = field switch
        {
            ItemSortField.Relevance => earlier.Relevance.CompareTo(later.Relevance),
            ItemSortField.Date => earlier.PublishedAt.CompareTo(later.PublishedAt),
            _ => StringComparer.OrdinalIgnoreCase.Compare(earlier.Source, later.Source),
        };
        return direction == SortDirection.Ascending ? comparison <= 0 : comparison >= 0;
    }

    private static IDataSource CreateSource(string name) => TestSources.Returning(name, new SourceSuccess(ItemsOf(name)));

    private static IReadOnlyList<AggregatedItem> ItemsOf(string source) =>
        [.. Enumerable.Range(-3, 5).Select(hours => TestItems.Item(
            $"{source}{hours}",
            source: source,
            category: source,
            publishedAt: Base.AddHours(hours),
            relevance: (hours + 3 + source.Length % 3) / 7.0))];

    [GeneratedRegex(@"^(\w+):\s*(.+)$")]
    private static partial Regex ParameterLine();
}
