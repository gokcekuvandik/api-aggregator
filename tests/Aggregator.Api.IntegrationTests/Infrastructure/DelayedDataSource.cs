using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Answers like <paramref name="inner"/>, but only after <paramref name="delay"/>.</summary>
internal sealed class DelayedDataSource(IDataSource inner, TimeSpan delay) : IDataSource
{
    public string Name => inner.Name;

    public IReadOnlyList<string> Categories => inner.Categories;

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return await inner.FetchAsync(query, cancellationToken);
    }
}
