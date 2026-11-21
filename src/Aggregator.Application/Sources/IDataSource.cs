using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>Port for one external data source (ADR-0004).</summary>
/// <remarks>
/// <para>Register implementations as singletons: <c>AggregationService</c> is a singleton and
/// captures every source it is given.</para>
/// <para>Sources run concurrently and are not cancelled when a sibling throws; ADR-0006 adds
/// per-source timeouts and resilience. Until then, <see cref="SourceFailure.Error"/> is
/// returned to clients as-is, so it must be a short, client-safe message (no exception text).</para>
/// </remarks>
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
