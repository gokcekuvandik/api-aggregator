namespace Aggregator.Infrastructure.Sources.News.HackerNews;

internal sealed record HackerNewsSearch(IReadOnlyList<HackerNewsHit> Hits);
