using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>A source that answers with fixed items and remembers the query it was asked.</summary>
internal sealed class RecordingDataSource(string name, params AggregatedItem[] items) : IDataSource
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Categories { get; } = [name];

    public AggregationQuery? LastQuery { get; private set; }

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;
        return Task.FromResult<SourceResult>(new SourceSuccess(items));
    }

    public static AggregatedItem Item(string source, string id, DateTimeOffset publishedAt, double relevance) => new(
        Id: id,
        Source: source,
        Category: source,
        Title: $"{source} {id}",
        Summary: null,
        Url: null,
        PublishedAt: publishedAt,
        Relevance: RelevanceScore.From(relevance),
        Details: new NewsDetails(Author: null, Points: null, Publisher: null));
}
