namespace Aggregator.Api.Contracts;

/// <summary>How many requests fell into a bucket and their average duration; zero for both when the bucket is empty.</summary>
public sealed record BucketResponse(long Count, double AverageMs);
