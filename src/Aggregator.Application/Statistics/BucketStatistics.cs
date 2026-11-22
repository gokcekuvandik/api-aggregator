namespace Aggregator.Application.Statistics;

/// <summary>Completed requests that fell into one performance bucket.</summary>
public sealed record BucketStatistics(long Count, TimeSpan AverageDuration)
{
    public static BucketStatistics Empty { get; } = new(0, TimeSpan.Zero);
}
