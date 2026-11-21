using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Relevance;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.News.NewsApi;

internal sealed class NewsApiOptions : NewsOptions
{
    public NewsApiOptions() => BaseAddress = new Uri("https://newsapi.org/");

    [Required]
    [RegularExpression(HeaderSafePattern, ErrorMessage = HeaderSafeMessage)]
    public string? ApiKey { get; set; }
}

/// <summary>
/// Articles from NewsAPI; used instead of Hacker News when an API key is configured.
/// With search text the results are ranked by relevancy, otherwise by publication date.
/// </summary>
internal sealed class NewsApiSource(SourceFetcher fetcher, IOptions<NewsApiOptions> options, IRelevanceScorer<object> scorer) : IDataSource
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string RemovedMarker = "[Removed]";

    public string Name => SourceNames.News;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.News];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var request = new HttpRequestMessage(HttpMethod.Get, Path(query, options.Value));
        request.Headers.Add(ApiKeyHeader, options.Value.ApiKey);

        return fetcher.FetchAsync(
            Name,
            request,
            NewsApiJsonContext.Default.NewsApiResponse,
            response => NewsItemMapper.ToItems(Name, response.Articles.Where(IsAvailable).Select(ToArticle), scorer, query),
            cancellationToken);
    }

    private static string Path(AggregationQuery query, NewsApiOptions settings)
    {
        var sortBy = query.Text is null ? "publishedAt" : "relevancy";
        var path = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"v2/everything?q={Uri.EscapeDataString(query.Text ?? settings.Query)}&pageSize={settings.MaxItems}&sortBy={sortBy}&language=en"));
        if (query.From is { } from)
        {
            path.Append("&from=").Append(Uri.EscapeDataString(UpstreamTime.StartIso(from)));
        }

        if (query.To is { } to)
        {
            path.Append("&to=").Append(Uri.EscapeDataString(UpstreamTime.EndIso(to)));
        }

        return path.ToString();
    }

    private static bool IsAvailable(NewsApiArticle article) => !string.Equals(article.Title, RemovedMarker, StringComparison.Ordinal);

    private static NewsArticle ToArticle(NewsApiArticle article) => new(
        Id: article.Url ?? article.Title ?? string.Empty,
        Title: article.Title ?? string.Empty,
        Summary: article.Description,
        Url: SafeUri.Parse(article.Url),
        PublishedAt: article.PublishedAt,
        Author: article.Author,
        Publisher: article.Source?.Name);
}

internal sealed record NewsApiResponse(IReadOnlyList<NewsApiArticle> Articles);

internal sealed record NewsApiArticle(
    DateTimeOffset PublishedAt,
    string? Title = null,
    string? Description = null,
    string? Url = null,
    string? Author = null,
    NewsApiPublisher? Source = null);

internal sealed record NewsApiPublisher(string? Name = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NewsApiResponse))]
internal sealed partial class NewsApiJsonContext : JsonSerializerContext;
