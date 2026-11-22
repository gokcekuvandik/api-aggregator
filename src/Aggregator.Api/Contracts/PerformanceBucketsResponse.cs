namespace Aggregator.Api.Contracts;

/// <summary>Completed requests per performance bucket, by the configured thresholds (by default fast under 100 ms, slow over 200 ms).</summary>
public sealed record PerformanceBucketsResponse(BucketResponse Fast, BucketResponse Average, BucketResponse Slow);
