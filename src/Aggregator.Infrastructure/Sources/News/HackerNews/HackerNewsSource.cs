using System.Globalization;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.News.HackerNews;

internal sealed class HackerNewsOptions : NewsOptions
{
    public HackerNewsOptions() => BaseAddress = new Uri("https://hn.algolia.com/");
}

/// <summary>Hacker News stories through the Algolia search API, which needs no API key.</summary>
internal sealed class HackerNewsSource(SourceFetcher fetcher, IOptions<HackerNewsOptions> options) : IDataSource
{
    public string Name => SourceNames.News;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.News];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"api/v1/search?tags=story&query={Uri.EscapeDataString(settings.Query)}&hitsPerPage={settings.MaxItems}");

        return fetcher.FetchAsync(
            Name,
            new HttpRequestMessage(HttpMethod.Get, path),
            HackerNewsJsonContext.Default.HackerNewsSearch,
            search => NewsItemMapper.ToItems(Name, search.Hits.Select(ToArticle)),
            cancellationToken);
    }

    private static NewsArticle ToArticle(HackerNewsHit hit) => new(
        Id: hit.ObjectId,
        Title: hit.Title ?? string.Empty,
        Summary: hit is { Points: { } points, Author: { } author } ? string.Create(CultureInfo.InvariantCulture, $"{points} points by {author}") : null,
        Url: SafeUri.Parse(hit.Url) ?? new Uri($"https://news.ycombinator.com/item?id={Uri.EscapeDataString(hit.ObjectId)}"),
        PublishedAt: hit.CreatedAt);
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
