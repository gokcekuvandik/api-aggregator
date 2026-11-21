namespace Aggregator.Domain.Items;

/// <summary>
/// One item in the unified result set, whatever source it came from.
/// ADR-0005 adds <c>Relevance</c> and the polymorphic <c>Details</c>.
/// </summary>
public sealed record AggregatedItem(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt);
