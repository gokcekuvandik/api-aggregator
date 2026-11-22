namespace Aggregator.Infrastructure.Sources.News.NewsApi;

internal sealed record NewsApiResponse(IReadOnlyList<NewsApiArticle> Articles);
