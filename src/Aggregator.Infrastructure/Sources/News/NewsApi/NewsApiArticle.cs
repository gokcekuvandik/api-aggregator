namespace Aggregator.Infrastructure.Sources.News.NewsApi;

internal sealed record NewsApiArticle(
    DateTimeOffset PublishedAt,
    string? Title = null,
    string? Description = null,
    string? Url = null,
    string? Author = null,
    NewsApiPublisher? Source = null);
