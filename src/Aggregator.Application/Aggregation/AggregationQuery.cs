using Aggregator.Application.Aggregation.Sorting;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// What to fetch, filter and sort, and which page to return. Sources may use the search fields to narrow their requests.
/// Text fields are trimmed, and blank values become null.
/// </summary>
public sealed record AggregationQuery(int Page, int PageSize)
{
    /// <summary>Free-text search passed to sources that support it.</summary>
    public string? Text { get; init => field = Normalize(value); }

    /// <summary>City for weather sources; null means the configured default.</summary>
    public string? City { get; init => field = Normalize(value); }

    /// <summary>Source names to query; empty means all.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    public string? Category { get; init => field = Normalize(value); }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public ItemSortField SortBy { get; init; } = ItemSortField.Relevance;

    public SortDirection SortOrder { get; init; } = SortDirection.Descending;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
