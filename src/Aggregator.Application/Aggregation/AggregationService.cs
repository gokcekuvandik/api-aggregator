using System.Diagnostics;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// Fans out to every registered source concurrently, merges their items and pages the result.
/// Per-source timeouts and fallback arrive with ADR-0006; filtering and sorting with ADR-0005.
/// </summary>
public sealed class AggregationService(IEnumerable<IDataSource> sources)
{
    private readonly IReadOnlyList<IDataSource> _sources = [.. sources];

    public async Task<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var reports = await Task.WhenAll(_sources.Select(source => FetchAsync(source, query, cancellationToken)));
        var merged = reports.SelectMany(report => report.Result.Items).ToList();

        return new AggregationResult(PageRequest.Apply(merged, query.Page, query.PageSize), reports);
    }

    private static async Task<SourceReport> FetchAsync(IDataSource source, AggregationQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await source.FetchAsync(query, cancellationToken);
        return new SourceReport(source.Name, result, Stopwatch.GetElapsedTime(started));
    }
}
