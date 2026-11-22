namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

internal sealed record GitHubRepository(
    long Id,
    string FullName,
    DateTimeOffset UpdatedAt,
    string? HtmlUrl = null,
    string? Description = null,
    double Score = 0,
    int StargazersCount = 0,
    int ForksCount = 0,
    string? Language = null);
