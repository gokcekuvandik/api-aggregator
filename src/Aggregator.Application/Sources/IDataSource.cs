using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>
/// One external data source. Register implementations as singletons.
/// <see cref="SourceFailure.Error"/> is returned to clients as-is, so keep it short and safe.
/// </summary>
public interface IDataSource
{
    string Name { get; }

    IReadOnlyList<string> Categories { get; }

    /// <summary>The search fields this source sends upstream or scores with; only these make up its cache key. All of them by default.</summary>
    SearchFields UsedFields => SearchFields.All;

    /// <summary>Returns expected failures as <see cref="SourceFailure"/>; an exception becomes a 500.</summary>
    Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken);
}
