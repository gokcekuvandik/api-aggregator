namespace Aggregator.Application.Statistics;

/// <summary>Read side of the request statistics store (ADR-0008).</summary>
public interface IStatisticsReader
{
    /// <summary>An immutable copy of the current statistics, one entry per source.</summary>
    IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots();
}

/// <summary>
/// Statistics for one source. ADR-0008 adds the performance buckets and the overall class.
/// </summary>
public sealed record ApiStatisticsSnapshot(
    string Source,
    long TotalRequests,
    TimeSpan AverageDuration,
    long CacheHits,
    long Failures);
