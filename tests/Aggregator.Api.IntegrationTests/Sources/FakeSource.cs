using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources;

namespace Aggregator.Api.IntegrationTests.Sources;

internal sealed class FakeSourceOptions : DataSourceOptions
{
    public FakeSourceOptions() => BaseAddress = new Uri("http://localhost:1/");
}

/// <summary>A fourth source written the way the README guide describes, used to prove a source needs only one registration line.</summary>
internal sealed class FakeSource(IHttpClientFactory httpClientFactory) : IDataSource
{
    public const string SourceName = "fake";
    public const string Path = "/fake/titles";

    public string Name => SourceName;

    public IReadOnlyList<string> Categories { get; } = ["fake"];

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var titles = await httpClientFactory.CreateClient(Name).GetFromJsonAsync<string[]>(Path.TrimStart('/'), cancellationToken) ?? [];
        return new SourceSuccess([.. titles.Select((title, i) => new AggregatedItem(
            $"{Name}:{i}", Name, Name, title, null, null, new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
            RelevanceScore.None, new NewsDetails(null, null, null)))]);
    }
}
