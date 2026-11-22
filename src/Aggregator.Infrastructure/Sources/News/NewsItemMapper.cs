using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;

namespace Aggregator.Infrastructure.Sources.News;

/// <summary>Turns articles from either provider into items, skipping those without a title and scoring the rest by rank.</summary>
internal static class NewsItemMapper
{
    public static IReadOnlyList<AggregatedItem> ToItems(
        string sourceName, IEnumerable<NewsArticle> articles, IRelevanceScorer<object> scorer, AggregationQuery query)
    {
        List<NewsArticle> titled = [.. articles.Where(article => !string.IsNullOrWhiteSpace(article.Title))];
        var scores = scorer.Score(titled, query);

        return [.. titled.Select((article, i) => new AggregatedItem(
            Id: $"{sourceName}:{article.Id}",
            Source: sourceName,
            Category: SourceNames.News,
            Title: article.Title.Trim(),
            Summary: article.Summary,
            Url: article.Url,
            PublishedAt: article.PublishedAt,
            Relevance: scores[i],
            Details: new NewsDetails(article.Author, article.Points, article.Publisher)))];
    }
}
