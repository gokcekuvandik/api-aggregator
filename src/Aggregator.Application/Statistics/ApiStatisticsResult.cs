using Aggregator.Domain.Statistics;

namespace Aggregator.Application.Statistics;

/// <summary>
/// Statistics for one external API since startup. Requests count those the upstream answered or failed, so the buckets add up to them.
/// Requests the caller cancelled, and requests rejected before any attempt reached the upstream (an open circuit, for example), are counted apart and never timed.
/// <see cref="Performance"/> classifies the average and is null before the first timed request.
/// </summary>
public sealed record ApiStatisticsResult(
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
