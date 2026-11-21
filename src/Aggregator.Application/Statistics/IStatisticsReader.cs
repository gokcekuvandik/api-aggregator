namespace Aggregator.Application.Statistics;

/// <summary>Read side of the request statistics store.</summary>
public interface IStatisticsReader
{
    /// <summary>An immutable copy of the current statistics, one entry per source.</summary>
    IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots();
}

/// <summary>Statistics for one source.</summary>
public sealed record ApiStatisticsSnapshot(
    string Source,
    long TotalRequests,
    TimeSpan AverageDuration,
    long CacheHits,
    long Failures);
