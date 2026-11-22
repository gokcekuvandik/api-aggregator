using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;

namespace Aggregator.Api.Contracts;

/// <summary>
/// Requests to one external API since this instance started: the source's primary provider when <c>provider</c> is null, otherwise the named secondary.
/// <c>totalRequests</c>, <c>averageMs</c> and the buckets cover requests the API answered or failed, retries included in their time.
/// <c>cancelled</c> (the caller gave up) and <c>rejected</c> (no attempt was sent, for example an open circuit) are counted apart; cache hits never reach the API.
/// </summary>
public sealed record SourceStatisticsResponse(
    string Source,
    string? Provider,
    long TotalRequests,
    double AverageMs,
    Performance? Performance,
    PerformanceBucketsResponse Buckets,
    long Retries,
    long Failures,
    long Cancelled,
    long Rejected,
    long CacheHits)
{
    public static SourceStatisticsResponse From(ApiStatisticsResult snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Api.Source,
            snapshot.Api.Provider,
            snapshot.TotalRequests,
            snapshot.AverageDuration.TotalMilliseconds,
            ToContract(snapshot.Performance),
            new PerformanceBucketsResponse(Bucket(snapshot.Fast), Bucket(snapshot.Average), Bucket(snapshot.Slow)),
            snapshot.Retries,
            snapshot.Failures,
            snapshot.Cancelled,
            snapshot.Rejected,
            snapshot.CacheHits);
    }

    private static BucketResponse Bucket(BucketStatistics bucket) => new(bucket.Count, bucket.AverageDuration.TotalMilliseconds);

    private static Performance? ToContract(PerformanceBucket? bucket) => bucket switch
    {
        null => null,
        PerformanceBucket.Fast => Contracts.Performance.Fast,
        PerformanceBucket.Average => Contracts.Performance.Average,
        PerformanceBucket.Slow => Contracts.Performance.Slow,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown performance bucket."),
    };
}
