using System.ComponentModel.DataAnnotations;
using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;

namespace Aggregator.Infrastructure.Sources.News;

/// <summary>What to search for and how many articles to take; shared by both news providers.</summary>
internal abstract class NewsOptions : DataSourceOptions
{
    [Required]
    public string Query { get; set; } = "dotnet";

    [Range(1, 100)]
    public int MaxItems { get; set; } = 20;
}

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
