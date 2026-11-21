using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>A source that always answers with the same <see cref="SourceResult"/>.</summary>
internal sealed class StubDataSource(string name, SourceResult result) : IDataSource
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Categories { get; } = [name];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(result);

    public static StubDataSource InState(string name, string state, int itemCount = 1) => new(name, state switch
    {
        "Success" => new SourceSuccess(Items(name, itemCount)),
        "Cached" => new SourceCached(Items(name, itemCount), TimeSpan.FromMinutes(2)),
        "Fallback" => new SourceFallback(Items(name, itemCount), TimeSpan.FromSeconds(90)),
        "Failed" => new SourceFailure($"{name} is unavailable"),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown source state."),
    });

    private static IReadOnlyList<AggregatedItem> Items(string source, int count) =>
        [.. Enumerable.Range(1, count).Select(i => new AggregatedItem(
            Id: $"{source}-{i}",
            Source: source,
            Category: source,
            Title: $"{source} item {i}",
            Summary: "summary",
            Url: new Uri($"https://example.test/{source}/{i}"),
            PublishedAt: new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero),
            Relevance: RelevanceScore.From(0.5),
            Details: DetailsFor(source)))];

    private static ItemDetails DetailsFor(string source) => source switch
    {
        "weather" => new WeatherDetails("Athens", 12, 19, "clear sky"),
        "repositories" => new RepositoryDetails(Stars: 42, Forks: 7, Language: "C#"),
        _ => new NewsDetails(Author: "alice", Points: 10, Publisher: null),
    };
}

/// <summary>A source that throws, standing in for an unexpected bug.</summary>
internal sealed class ThrowingDataSource : IDataSource
{
    public const string SecretMessage = "connection string Server=db;Password=hunter2";

    public string Name => "broken";

    public IReadOnlyList<string> Categories { get; } = ["broken"];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);
}
