using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.News.NewsApi;

internal sealed class NewsApiOptions : NewsOptions
{
    public NewsApiOptions() => BaseAddress = new Uri("https://newsapi.org/");

    [Required]
    [RegularExpression(HeaderSafePattern, ErrorMessage = HeaderSafeMessage)]
    public string? ApiKey { get; set; }
}

/// <summary>Articles from NewsAPI; used instead of Hacker News when an API key is configured.</summary>
internal sealed class NewsApiSource(SourceFetcher fetcher, IOptions<NewsApiOptions> options) : IDataSource
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string RemovedMarker = "[Removed]";

    public string Name => SourceNames.News;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.News];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"v2/everything?q={Uri.EscapeDataString(settings.Query)}&pageSize={settings.MaxItems}&sortBy=publishedAt&language=en");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ApiKeyHeader, settings.ApiKey);

        return fetcher.FetchAsync(
            Name,
            request,
            NewsApiJsonContext.Default.NewsApiResponse,
            response => NewsItemMapper.ToItems(Name, response.Articles.Where(IsAvailable).Select(ToArticle)),
            cancellationToken);
    }

    private static bool IsAvailable(NewsApiArticle article) => !string.Equals(article.Title, RemovedMarker, StringComparison.Ordinal);

    private static NewsArticle ToArticle(NewsApiArticle article) => new(
        Id: article.Url ?? article.Title ?? string.Empty,
        Title: article.Title ?? string.Empty,
        Summary: article.Description,
        Url: SafeUri.Parse(article.Url),
        PublishedAt: article.PublishedAt);
}

internal sealed record NewsApiResponse(IReadOnlyList<NewsApiArticle> Articles);

internal sealed record NewsApiArticle(DateTimeOffset PublishedAt, string? Title = null, string? Description = null, string? Url = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NewsApiResponse))]
internal sealed partial class NewsApiJsonContext : JsonSerializerContext;
