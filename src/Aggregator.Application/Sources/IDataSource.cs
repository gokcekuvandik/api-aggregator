using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>Port for one external data source (ADR-0004).</summary>
public interface IDataSource
{
    string Name { get; }

    IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Fetches items for the query. Expected failures are returned as <see cref="SourceFailure"/>;
    /// an exception means something unexpected and surfaces as a 500 ProblemDetails.
    /// </summary>
    Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken);
}
