using System.Globalization;
using System.Net.Http.Headers;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

/// <summary>
/// Repositories matching a GitHub search: the configured query sorted by stars, or the request's text by best match.
/// </summary>
internal sealed class GitHubSource(SourceFetcher fetcher, IOptions<GitHubOptions> options, IRelevanceScorer<RepositorySignal> scorer) : IDataSource
{
    private const string MediaType = "application/vnd.github+json";
    private const string ApiVersionHeader = "X-GitHub-Api-Version";
    private const string ApiVersion = "2022-11-28";

    public string Name => SourceNames.Repositories;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Repositories];

    public SearchFields UsedFields => SearchFields.Text;

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = options.Value;
        var sort = query.Text is null ? "&sort=stars&order=desc" : string.Empty;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"search/repositories?q={Uri.EscapeDataString(query.Text ?? settings.Query)}{sort}&per_page={settings.MaxItems}");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        request.Headers.Add(ApiVersionHeader, ApiVersion);
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        return fetcher.FetchAsync(
            settings.ClientFor(Name),
            request,
            GitHubJsonContext.Default.GitHubSearch,
            search => ToItems(search.Items, query),
            cancellationToken);
    }

    private IReadOnlyList<AggregatedItem> ToItems(IReadOnlyList<GitHubRepository> repositories, AggregationQuery query)
    {
        var scores = scorer.Score([.. repositories.Select(repository => new RepositorySignal(repository.Score, repository.StargazersCount))], query);
        return [.. repositories.Select((repository, i) => ToItem(repository, scores[i]))];
    }

    private AggregatedItem ToItem(GitHubRepository repository, RelevanceScore relevance) => new(
        Id: string.Create(CultureInfo.InvariantCulture, $"{Name}:{repository.Id}"),
        Source: Name,
        Category: SourceNames.Repositories,
        Title: repository.FullName,
        Summary: repository.Description,
        Url: SafeUri.Parse(repository.HtmlUrl),
        PublishedAt: repository.UpdatedAt,
        Relevance: relevance,
        Details: new RepositoryDetails(repository.StargazersCount, repository.ForksCount, repository.Language));
}
