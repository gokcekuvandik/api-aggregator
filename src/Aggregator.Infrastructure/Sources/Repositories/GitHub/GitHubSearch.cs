namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

internal sealed record GitHubSearch(IReadOnlyList<GitHubRepository> Items);
