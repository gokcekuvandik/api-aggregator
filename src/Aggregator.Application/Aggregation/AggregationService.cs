using System.Diagnostics;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;

namespace Aggregator.Application.Aggregation;

/// <summary>Queries every source concurrently, merges their items and pages the result.</summary>
public sealed class AggregationService(DataSourceRegistry registry)
{
    public async Task<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var reports = await Task.WhenAll(registry.All.Select(source => FetchAsync(source, query, cancellationToken)));
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
