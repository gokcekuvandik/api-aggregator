namespace Aggregator.Application.UnitTests.TestData;

/// <summary>One row of the pairwise model in <c>aggregation.pict</c>.</summary>
public sealed record PairwiseCase(string SortBy, string SortOrder, string Category, string Sources, string DateRange)
{
    public string[] Values => [SortBy, SortOrder, Category, Sources, DateRange];

    public override string ToString() => string.Join(" / ", Values);
}

/// <summary>All-pairs rows for <c>aggregation.pict</c>, generated once and committed.</summary>
internal static class AggregationPairwiseCases
{
    public const string ModelFile = "aggregation.pict";

    public static readonly string[] Parameters = ["SortBy", "SortOrder", "Category", "Sources", "DateRange"];

    public static IReadOnlyList<PairwiseCase> Rows { get; } =
    [
        new("relevance", "asc", "none", "all", "none"),
        new("relevance", "desc", "news", "news", "fromOnly"),
        new("date", "asc", "weather", "weather+repositories", "fromOnly"),
        new("source", "desc", "none", "weather+repositories", "toOnly"),
        new("date", "desc", "news", "all", "both"),
        new("source", "asc", "news", "news", "equal"),
        new("relevance", "desc", "weather", "all", "equal"),
        new("date", "asc", "none", "news", "toOnly"),
        new("relevance", "asc", "none", "weather+repositories", "both"),
        new("source", "desc", "weather", "all", "none"),
        new("relevance", "asc", "news", "all", "toOnly"),
        new("date", "asc", "none", "weather+repositories", "equal"),
        new("date", "asc", "news", "news", "none"),
        new("source", "asc", "none", "all", "fromOnly"),
        new("source", "asc", "none", "news", "both"),
        new("relevance", "asc", "none", "weather+repositories", "none"),
        new("relevance", "asc", "weather", "all", "toOnly"),
        new("relevance", "asc", "weather", "all", "both"),
    ];

    /// <summary>The model's constraints, mirrored for the coverage check.</summary>
    public static bool IsAllowed(IReadOnlyList<string> values) =>
        !(values[3] == "news" && values[2] == "weather")
        && !(values[3] == "weather+repositories" && values[2] == "news");

    public static TheoryData<PairwiseCase> Data => [.. Rows];
}
