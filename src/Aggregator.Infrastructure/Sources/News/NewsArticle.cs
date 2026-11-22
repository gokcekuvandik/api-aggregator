namespace Aggregator.Infrastructure.Sources.News;

/// <summary>One article, already in provider-neutral form and in the provider's rank order.</summary>
internal sealed record NewsArticle(
    string Id,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    string? Author = null,
    int? Points = null,
    string? Publisher = null);
