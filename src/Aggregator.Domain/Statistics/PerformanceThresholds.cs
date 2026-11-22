namespace Aggregator.Domain.Statistics;

/// <summary>
/// Boundaries between the performance buckets: below <see cref="FastBelow"/> is fast, up to and including <see cref="SlowAbove"/> is average, and anything longer is slow.
/// The defaults are 100 ms and 200 ms.
/// </summary>
public sealed record PerformanceThresholds
{
    public PerformanceThresholds(TimeSpan fastBelow, TimeSpan slowAbove)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fastBelow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(slowAbove, fastBelow);

        FastBelow = fastBelow;
        SlowAbove = slowAbove;
    }

    public static PerformanceThresholds Default { get; } = new(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));

    public TimeSpan FastBelow { get; }

    public TimeSpan SlowAbove { get; }

    public PerformanceBucket Classify(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        if (duration < FastBelow)
        {
            return PerformanceBucket.Fast;
        }

        return duration <= SlowAbove ? PerformanceBucket.Average : PerformanceBucket.Slow;
    }
}
