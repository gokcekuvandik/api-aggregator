using System.Globalization;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Relevance;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.News.HackerNews;

internal sealed class HackerNewsOptions : NewsOptions
{
    public HackerNewsOptions() => BaseAddress = new Uri("https://hn.algolia.com/");
}

/// <summary>Hacker News stories through the Algolia search API, which needs no API key and ranks by relevance.</summary>
internal sealed class HackerNewsSource(SourceFetcher fetcher, IOptions<HackerNewsOptions> options, IRelevanceScorer<object> scorer) : IDataSource
{
    public string Name => SourceNames.News;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.News];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"api/v1/search?tags=story&query={Uri.EscapeDataString(query.Text ?? settings.Query)}&hitsPerPage={settings.MaxItems}{CreationFilter(query)}");

        return fetcher.FetchAsync(
            Name,
            new HttpRequestMessage(HttpMethod.Get, path),
            HackerNewsJsonContext.Default.HackerNewsSearch,
            search => NewsItemMapper.ToItems(Name, search.Hits.Select(ToArticle), scorer, query),
            cancellationToken);
    }

    private static string CreationFilter(AggregationQuery query)
    {
        List<string> filters = [];
        if (query.From is { } from)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"created_at_i>={UpstreamTime.StartUnixSeconds(from)}"));
        }

        if (query.To is { } to)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"created_at_i<={UpstreamTime.EndUnixSeconds(to)}"));
        }

        return filters.Count == 0 ? string.Empty : $"&numericFilters={Uri.EscapeDataString(string.Join(',', filters))}";
    }

    private static NewsArticle ToArticle(HackerNewsHit hit) => new(
        Id: hit.ObjectId,
        Title: hit.Title ?? string.Empty,
        Summary: hit is { Points: { } points, Author: { } author } ? string.Create(CultureInfo.InvariantCulture, $"{points} points by {author}") : null,
        Url: SafeUri.Parse(hit.Url) ?? new Uri($"https://news.ycombinator.com/item?id={Uri.EscapeDataString(hit.ObjectId)}"),
        PublishedAt: hit.CreatedAt,
        Author: hit.Author,
        Points: hit.Points);
}

internal sealed record HackerNewsSearch(IReadOnlyList<HackerNewsHit> Hits);

internal sealed record HackerNewsHit(
    [property: JsonPropertyName("objectID")] string ObjectId,
    DateTimeOffset CreatedAt,
    string? Title = null,
    string? Url = null,
    string? Author = null,
    int? Points = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(HackerNewsSearch))]
internal sealed partial class HackerNewsJsonContext : JsonSerializerContext;
