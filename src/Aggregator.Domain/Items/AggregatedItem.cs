namespace Aggregator.Domain.Items;

/// <summary>One item in the unified result set, whatever source it came from.</summary>
public sealed record AggregatedItem(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    RelevanceScore Relevance,
    ItemDetails Details);
