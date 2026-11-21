using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

internal sealed class GitHubOptions : DataSourceOptions
{
    public GitHubOptions() => BaseAddress = new Uri("https://api.github.com/");

    [Required]
    public string Query { get; set; } = "topic:dotnet";

    [Range(1, 100)]
    public int MaxItems { get; set; } = 20;

    /// <summary>Optional; without it requests are anonymous and have a lower rate limit.</summary>
    [RegularExpression(HeaderSafePattern, ErrorMessage = HeaderSafeMessage)]
    public string? Token { get; set; }
}

/// <summary>The most-starred repositories matching a GitHub search query.</summary>
internal sealed class GitHubSource(SourceFetcher fetcher, IOptions<GitHubOptions> options) : IDataSource
{
    private const string MediaType = "application/vnd.github+json";
    private const string ApiVersionHeader = "X-GitHub-Api-Version";
    private const string ApiVersion = "2022-11-28";

    public string Name => SourceNames.Repositories;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Repositories];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"search/repositories?q={Uri.EscapeDataString(settings.Query)}&sort=stars&order=desc&per_page={settings.MaxItems}");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        request.Headers.Add(ApiVersionHeader, ApiVersion);
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        return fetcher.FetchAsync(
            Name,
            request,
            GitHubJsonContext.Default.GitHubSearch,
            search => [.. search.Items.Select(ToItem)],
            cancellationToken);
    }

    private AggregatedItem ToItem(GitHubRepository repository) => new(
        Id: string.Create(CultureInfo.InvariantCulture, $"{Name}:{repository.Id}"),
        Source: Name,
        Category: SourceNames.Repositories,
        Title: repository.FullName,
        Summary: repository.Description,
        Url: SafeUri.Parse(repository.HtmlUrl),
        PublishedAt: repository.UpdatedAt);
}

internal sealed record GitHubSearch(IReadOnlyList<GitHubRepository> Items);

internal sealed record GitHubRepository(long Id, string FullName, DateTimeOffset UpdatedAt, string? HtmlUrl = null, string? Description = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GitHubSearch))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
