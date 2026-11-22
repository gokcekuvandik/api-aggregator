using Aggregator.Domain.Statistics;

namespace Aggregator.Application.Statistics;

/// <summary>Read side of the request statistics store.</summary>
public interface IStatisticsReader
{
    /// <summary>An immutable copy of the current statistics, one entry per external API.</summary>
    IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots();
}

/// <summary>
/// Statistics for one external API since startup. Requests count those the upstream answered or failed, so the buckets add up to them.
/// Requests the caller cancelled, and requests rejected before any attempt reached the upstream (an open circuit, for example), are counted apart and never timed.
/// <see cref="Performance"/> classifies the average and is null before the first timed request.
/// </summary>
public sealed record ApiStatisticsSnapshot(
    UpstreamApi Api,
    long TotalRequests,
    TimeSpan AverageDuration,
    PerformanceBucket? Performance,
    BucketStatistics Fast,
    BucketStatistics Average,
    BucketStatistics Slow,
    long Retries,
    long Failures,
    long Cancelled,
    long Rejected,
    long CacheHits,
    WindowStatistics Recent);

/// <summary>Completed requests that fell into one performance bucket.</summary>
public sealed record BucketStatistics(long Count, TimeSpan AverageDuration)
{
    public static BucketStatistics Empty { get; } = new(0, TimeSpan.Zero);
}

/// <summary>Completed requests within the last <see cref="Length"/>.</summary>
public sealed record WindowStatistics(TimeSpan Length, long Count, TimeSpan AverageDuration);
