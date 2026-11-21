using System.ComponentModel.DataAnnotations;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.News;

/// <summary>What to search for and how many articles to take; shared by both news providers.</summary>
internal abstract class NewsOptions : DataSourceOptions
{
    [Required]
    public string Query { get; set; } = "dotnet";

    [Range(1, 100)]
    public int MaxItems { get; set; } = 20;
}

/// <summary>One article, already in provider-neutral form.</summary>
internal sealed record NewsArticle(string Id, string Title, string? Summary, Uri? Url, DateTimeOffset PublishedAt);

/// <summary>Turns articles from either provider into items, skipping those without a title.</summary>
internal static class NewsItemMapper
{
    public static IReadOnlyList<AggregatedItem> ToItems(string sourceName, IEnumerable<NewsArticle> articles) =>
        [.. articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Title))
            .Select(article => new AggregatedItem(
                Id: $"{sourceName}:{article.Id}",
                Source: sourceName,
                Category: SourceNames.News,
                Title: article.Title.Trim(),
                Summary: article.Summary,
                Url: article.Url,
                PublishedAt: article.PublishedAt))];
}
